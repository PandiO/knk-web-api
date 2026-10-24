using System.Threading.Channels;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.CloudflareAccess
{
    /// <summary>
    /// The Cloudflare Access sync as the rest of the API sees it (KNG-216, DESIGN.md § 5.8). Every
    /// method is a no-op when CloudflareAccess:SyncEnabled is false.
    /// </summary>
    public interface ICloudflareAccessSync
    {
        /// <summary>Sync is on and configured.</summary>
        bool Enabled { get; }

        /// <summary>CloudflareAccess:InlineAddTimeoutSeconds (5).</summary>
        TimeSpan InlineAddTimeout { get; }

        /// <summary>Asks for a reconcile run soon (an invite, an email or an account changed).</summary>
        void RequestSync();

        /// <summary>
        /// Registration's inline add: tries to put <paramref name="email"/> on the list within
        /// <paramref name="timeout"/>. Never throws; false means the next reconcile adds it.
        /// </summary>
        Task<bool> TryAddNowAsync(string? email, TimeSpan timeout);

        CloudflareAccessSyncStatusDto GetStatus();
    }

    /// <summary>What one reconcile run did.</summary>
    public sealed record CloudflareAccessRunResult(int Added, int Removed, int ListSize, bool RemovalsSkipped);

    /// <summary>
    /// The singleton behind <see cref="ICloudflareAccessSync"/>: it holds the status the admin page
    /// shows and serialises every change to the list, so a reconcile and an inline add never
    /// interleave. <see cref="CloudflareAccessSyncService"/> calls <see cref="ReconcileAsync"/>.
    /// </summary>
    public sealed class CloudflareAccessSync : ICloudflareAccessSync
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ICloudflareAccessList _list;
        private readonly CloudflareAccessSettings _settings;
        private readonly TimeProvider _time;
        private readonly ILogger<CloudflareAccessSync> _logger;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite
        });
        private readonly object _statusLock = new();

        private DateTime? _lastSuccessAt;
        private DateTime? _lastAttemptAt;
        private string? _lastError;
        private int _pendingAdds;
        private int _pendingRemoves;
        private int? _listSize;

        public CloudflareAccessSync(IServiceScopeFactory scopes, ICloudflareAccessList list,
            IOptions<CloudflareAccessSettings> settings, TimeProvider time, ILogger<CloudflareAccessSync> logger)
        {
            _scopes = scopes;
            _list = list;
            _settings = settings.Value;
            _time = time;
            _logger = logger;
            ConfigurationProblem = _settings.ConfigurationProblem();
            Enabled = _settings.SyncEnabled && ConfigurationProblem == null;
        }

        public bool Enabled { get; }

        /// <summary>Why an enabled sync can't run (missing id or token), or null.</summary>
        public string? ConfigurationProblem { get; }

        public CloudflareAccessSettings Settings => _settings;

        public TimeSpan InlineAddTimeout => _settings.InlineAddTimeout;

        /// <summary>Woken by <see cref="RequestSync"/>; read by the background service.</summary>
        public ChannelReader<bool> WakeReader => _wake.Reader;

        public static string Normalize(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

        private DateTime Now => _time.GetUtcNow().UtcDateTime;

        public void RequestSync()
        {
            if (Enabled) _wake.Writer.TryWrite(true);
        }

        public async Task<bool> TryAddNowAsync(string? email, TimeSpan timeout)
        {
            var normalized = Normalize(email);
            if (!Enabled || normalized.Length == 0) return false;

            using var cts = new CancellationTokenSource(timeout);
            var entered = false;
            try
            {
                entered = await _gate.WaitAsync(timeout);
                if (!entered)
                {
                    _logger.LogInformation("Cloudflare Access inline add skipped: a sync run is busy; the reconcile adds it");
                    RequestSync();
                    return false;
                }
                await _list.AddAsync(new[] { normalized }, cts.Token);
                _logger.LogInformation("Cloudflare Access inline add done for a new registration");
                return true;
            }
            catch (Exception ex)
            {
                // Never fail or hold up a registration: the reconcile catches it.
                _logger.LogWarning("Cloudflare Access inline add failed ({Error}); the reconcile adds it",
                    ex is OperationCanceledException ? "timed out" : ex.Message);
                RequestSync();
                return false;
            }
            finally
            {
                if (entered) _gate.Release();
            }
        }

        /// <summary>
        /// One reconcile run: desired set from the database, the list from Cloudflare, add what's
        /// missing, remove what's extra (only in this list). More removals than MaxRemovalsPerRun
        /// skips the removals and records an error. Throws on failure after recording it.
        /// </summary>
        public async Task<CloudflareAccessRunResult> ReconcileAsync(CancellationToken ct = default)
        {
            if (!Enabled) return new CloudflareAccessRunResult(0, 0, 0, false);

            await _gate.WaitAsync(ct);
            try
            {
                var now = Now;
                lock (_statusLock) _lastAttemptAt = now;

                HashSet<string> desired;
                using (var scope = _scopes.CreateScope())
                {
                    desired = await scope.ServiceProvider.GetRequiredService<ICloudflareAccessDesiredSet>().GetAsync(now, ct);
                }
                var current = await _list.GetEmailsAsync(ct);
                var (toAdd, toRemove) = Diff(desired, current);
                SetPending(toAdd.Count, toRemove.Count, current.Count);

                if (toAdd.Count > 0)
                {
                    await _list.AddAsync(toAdd, ct);
                }
                var skipRemovals = toRemove.Count > Math.Max(0, _settings.MaxRemovalsPerRun);
                if (toRemove.Count > 0 && !skipRemovals)
                {
                    await _list.RemoveAsync(toRemove, ct);
                }

                var size = current.Count + toAdd.Count - (skipRemovals ? 0 : toRemove.Count);
                lock (_statusLock)
                {
                    _pendingAdds = 0;
                    _pendingRemoves = skipRemovals ? toRemove.Count : 0;
                    _listSize = size;
                    if (skipRemovals)
                    {
                        _lastError = $"{toRemove.Count} removals held back (more than CloudflareAccess:MaxRemovalsPerRun = {_settings.MaxRemovalsPerRun}). Check the list, then raise the limit for one run.";
                    }
                    else
                    {
                        _lastError = null;
                        _lastSuccessAt = Now;
                    }
                }
                if (skipRemovals)
                {
                    _logger.LogError("Cloudflare Access sync held back {Count} removals (MaxRemovalsPerRun {Max})", toRemove.Count, _settings.MaxRemovalsPerRun);
                }
                else if (toAdd.Count > 0 || toRemove.Count > 0)
                {
                    _logger.LogInformation("Cloudflare Access sync: {Added} added, {Removed} removed, {Size} in the list", toAdd.Count, toRemove.Count, size);
                }
                return new CloudflareAccessRunResult(toAdd.Count, skipRemovals ? 0 : toRemove.Count, size, skipRemovals);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                lock (_statusLock) _lastError = ex is CloudflareApiException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
                throw;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// What to add (normalized emails in <paramref name="desired"/> missing from the list) and
        /// what to remove (list values, as stored, whose normalized form isn't desired).
        /// </summary>
        public static (List<string> ToAdd, List<string> ToRemove) Diff(IReadOnlyCollection<string> desired, IReadOnlyCollection<string> current)
        {
            var present = new HashSet<string>(current.Select(Normalize), StringComparer.Ordinal);
            var want = new HashSet<string>(desired.Select(Normalize).Where(e => e.Length > 0), StringComparer.Ordinal);
            var toAdd = want.Where(e => !present.Contains(e)).OrderBy(e => e, StringComparer.Ordinal).ToList();
            var toRemove = current.Where(v => !want.Contains(Normalize(v))).Distinct(StringComparer.Ordinal).ToList();
            return (toAdd, toRemove);
        }

        private void SetPending(int adds, int removes, int listSize)
        {
            lock (_statusLock)
            {
                _pendingAdds = adds;
                _pendingRemoves = removes;
                _listSize = listSize;
            }
        }

        public CloudflareAccessSyncStatusDto GetStatus()
        {
            lock (_statusLock)
            {
                return new CloudflareAccessSyncStatusDto
                {
                    Enabled = _settings.SyncEnabled,
                    Configured = ConfigurationProblem == null,
                    LastSuccessAt = _lastSuccessAt,
                    LastAttemptAt = _lastAttemptAt,
                    LastError = _settings.SyncEnabled ? ConfigurationProblem ?? _lastError : null,
                    PendingAdds = _pendingAdds,
                    PendingRemoves = _pendingRemoves,
                    ListSize = _listSize,
                    ReconcileMinutes = (int)_settings.ReconcileInterval.TotalMinutes
                };
            }
        }
    }
}
