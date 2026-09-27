using System.Security.Cryptography;
using System.Text;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// The ledger anomaly rules of docs/specs/currency-payments/DESIGN.md §3.9 (R3–R7), and the
    /// mapping of reconciler mismatches to R1/R2. Read-only: it returns findings and never changes
    /// a balance, a transaction or a policy (CurrencyAlertService stores the findings and applies
    /// the R1 kill switch). Each rule reads only its own time window, through the ledger's
    /// (CreatedAt), (ToUserId, CreatedAt) and (UserId, Currency, Id) indexes.
    /// <para>
    /// Thresholds come from <see cref="CurrencyMonitorOptions"/> (design defaults). Summaries
    /// name players by username where the rule involves more than the alert's own player.
    /// </para>
    /// </summary>
    public class CurrencyAnomalyDetector
    {
        private readonly KnKDbContext _context;
        private readonly CurrencyMonitorOptions _options;

        public CurrencyAnomalyDetector(KnKDbContext context, IOptions<CurrencyMonitorOptions>? options = null)
        {
            _context = context;
            _options = options?.Value ?? new CurrencyMonitorOptions();
        }

        /// <summary>
        /// R3: a player received transfers from at least FunnelMinNewSenders different players
        /// whose accounts are younger than FunnelNewAccountDays, within FunnelWindowHours.
        /// </summary>
        public async Task<List<CurrencyAlertDraft>> FunnelAsync(DateTime now, CancellationToken ct = default)
        {
            var window = TimeSpan.FromHours(_options.FunnelWindowHours);
            var since = now - window;
            var newSince = now - TimeSpan.FromDays(_options.FunnelNewAccountDays);
            var pairs = await (
                    from t in _context.CurrencyTransactions.AsNoTracking()
                    join u in _context.Users.AsNoTracking() on t.FromUserId equals (int?)u.Id
                    where t.Kind == CurrencyTransactionKind.Transfer && t.CreatedAt >= since && t.CreatedAt <= now
                          && t.ToUserId != null && u.CreatedAt >= newSince
                    select new { To = t.ToUserId!.Value, From = u.Id, t.Id })
                .ToListAsync(ct);

            var drafts = new List<CurrencyAlertDraft>();
            foreach (var group in pairs.GroupBy(p => p.To))
            {
                var senders = group.Select(p => p.From).Distinct().OrderBy(id => id).ToList();
                if (senders.Count < _options.FunnelMinNewSenders)
                {
                    continue;
                }
                var names = await NamesAsync(senders, ct);
                drafts.Add(new CurrencyAlertDraft(CurrencyAlertRules.Funnel, CurrencyAlertSeverity.High,
                    Truncate($"Received transfers from {senders.Count} accounts younger than {_options.FunnelNewAccountDays} days within {_options.FunnelWindowHours} h: {string.Join(", ", senders.Select(s => names.GetValueOrDefault(s, $"#{s}")))}."),
                    $"R3:user:{group.Key}", window)
                {
                    UserId = group.Key,
                    Details = new
                    {
                        newAccountSenders = senders.Select(s => new { userId = s, username = names.GetValueOrDefault(s) }),
                        transfers = group.Count(),
                        windowHours = _options.FunnelWindowHours,
                        newAccountDays = _options.FunnelNewAccountDays
                    }
                });
            }
            return drafts;
        }

        /// <summary>
        /// R4: two players sent each other money back and forth at least PingPongMinCycles times
        /// (min(A→B, B→A) transfers) within PingPongWindowMinutes.
        /// </summary>
        public async Task<List<CurrencyAlertDraft>> PingPongAsync(DateTime now, CancellationToken ct = default)
        {
            var window = TimeSpan.FromMinutes(_options.PingPongWindowMinutes);
            var since = now - window;
            var transfers = await _context.CurrencyTransactions.AsNoTracking()
                .Where(t => t.Kind == CurrencyTransactionKind.Transfer && t.CreatedAt >= since && t.CreatedAt <= now
                            && t.FromUserId != null && t.ToUserId != null)
                .Select(t => new { From = t.FromUserId!.Value, To = t.ToUserId!.Value })
                .ToListAsync(ct);

            var drafts = new List<CurrencyAlertDraft>();
            foreach (var pair in transfers.GroupBy(t => (A: Math.Min(t.From, t.To), B: Math.Max(t.From, t.To))))
            {
                var aToB = pair.Count(t => t.From == pair.Key.A);
                var bToA = pair.Count() - aToB;
                var cycles = Math.Min(aToB, bToA);
                if (cycles < _options.PingPongMinCycles)
                {
                    continue;
                }
                var names = await NamesAsync(new[] { pair.Key.A, pair.Key.B }, ct);
                drafts.Add(new CurrencyAlertDraft(CurrencyAlertRules.PingPong, CurrencyAlertSeverity.Medium,
                    Truncate($"{names.GetValueOrDefault(pair.Key.A, $"#{pair.Key.A}")} and {names.GetValueOrDefault(pair.Key.B, $"#{pair.Key.B}")} sent each other money {cycles} times back and forth within {_options.PingPongWindowMinutes} min ({aToB} and {bToA} transfers)."),
                    $"R4:pair:{pair.Key.A}:{pair.Key.B}", window)
                {
                    UserId = pair.Key.A,
                    Details = new
                    {
                        userA = pair.Key.A,
                        usernameA = names.GetValueOrDefault(pair.Key.A),
                        userB = pair.Key.B,
                        usernameB = names.GetValueOrDefault(pair.Key.B),
                        aToB,
                        bToA,
                        windowMinutes = _options.PingPongWindowMinutes
                    }
                });
            }
            return drafts;
        }

        /// <summary>
        /// R5: a player's net coin inflow within the last hour is above
        /// max(VelocityMinCoinsPerHour, VelocityMeanMultiplier × their hourly mean over the
        /// preceding VelocityBaselineDays). Every coin posting counts (transfers, rewards, staff grants).
        /// </summary>
        public async Task<List<CurrencyAlertDraft>> VelocityAsync(DateTime now, CancellationToken ct = default)
        {
            var hourStart = now - TimeSpan.FromHours(1);
            var candidates = await _context.CurrencyEntries.AsNoTracking()
                .Where(e => e.AccountKind == CurrencyAccountKind.User && e.Currency == Currency.Coins
                            && e.Transaction.CreatedAt >= hourStart && e.Transaction.CreatedAt <= now)
                .GroupBy(e => e.UserId)
                .Select(g => new { UserId = g.Key, Net = g.Sum(e => e.Amount) })
                .Where(x => x.Net > _options.VelocityMinCoinsPerHour)
                .ToListAsync(ct);
            if (candidates.Count == 0)
            {
                return new List<CurrencyAlertDraft>();
            }

            var baselineStart = now - TimeSpan.FromDays(_options.VelocityBaselineDays);
            var ids = candidates.Select(c => c.UserId).ToList();
            var baseline = await _context.CurrencyEntries.AsNoTracking()
                .Where(e => e.AccountKind == CurrencyAccountKind.User && e.Currency == Currency.Coins && ids.Contains(e.UserId)
                            && e.Transaction.CreatedAt >= baselineStart && e.Transaction.CreatedAt < hourStart)
                .GroupBy(e => e.UserId)
                .Select(g => new { UserId = g.Key, Net = g.Sum(e => e.Amount) })
                .ToDictionaryAsync(x => x.UserId ?? 0, x => x.Net, ct);
            var baselineHours = Math.Max(1, _options.VelocityBaselineDays * 24 - 1);

            var drafts = new List<CurrencyAlertDraft>();
            foreach (var candidate in candidates.Where(c => c.UserId.HasValue))
            {
                var userId = candidate.UserId!.Value;
                var mean = Math.Max(0, baseline.GetValueOrDefault(userId)) / (double)baselineHours;
                var threshold = Math.Max(_options.VelocityMinCoinsPerHour, (long)Math.Ceiling(_options.VelocityMeanMultiplier * mean));
                if (candidate.Net <= threshold)
                {
                    continue;
                }
                drafts.Add(new CurrencyAlertDraft(CurrencyAlertRules.Velocity, CurrencyAlertSeverity.High,
                    $"Net inflow of {candidate.Net:N0} coins within an hour (threshold {threshold:N0}, usual {mean:N0} an hour).",
                    $"R5:user:{userId}", TimeSpan.FromHours(1))
                {
                    UserId = userId,
                    Details = new { netInflow = candidate.Net, threshold, baselineHourlyMean = Math.Round(mean, 2), baselineDays = _options.VelocityBaselineDays }
                });
            }
            return drafts;
        }

        /// <summary>
        /// R6: a single staff adjustment above AdminSingleCoinsThreshold / AdminSingleGemsThreshold
        /// (either direction; checked over the last 24 h, one alert per transaction), or more than
        /// AdminMaxAdjustmentsPerHour adjustments by one staff member within an hour.
        /// </summary>
        public async Task<List<CurrencyAlertDraft>> AdminAsync(DateTime now, CancellationToken ct = default)
        {
            var coins = _options.AdminSingleCoinsThreshold;
            var gems = _options.AdminSingleGemsThreshold;
            var dayStart = now - TimeSpan.FromHours(24);
            var large = await _context.CurrencyEntries.AsNoTracking()
                .Where(e => e.AccountKind == CurrencyAccountKind.User && e.Transaction.Kind == CurrencyTransactionKind.AdminAdjust
                            && e.Transaction.CreatedAt >= dayStart && e.Transaction.CreatedAt <= now
                            && ((e.Currency == Currency.Coins && (e.Amount > coins || e.Amount < -coins))
                                || (e.Currency == Currency.Gems && (e.Amount > gems || e.Amount < -gems))))
                .Select(e => new
                {
                    e.TransactionId,
                    e.Transaction.PublicId,
                    e.Transaction.ReasonCode,
                    e.UserId,
                    e.Currency,
                    e.Amount,
                    e.Transaction.InitiatorUserId,
                    e.Transaction.InitiatorComponent
                })
                .ToListAsync(ct);

            var hourStart = now - TimeSpan.FromHours(1);
            var busy = await _context.CurrencyTransactions.AsNoTracking()
                .Where(t => t.Kind == CurrencyTransactionKind.AdminAdjust && t.CreatedAt >= hourStart && t.CreatedAt <= now)
                .GroupBy(t => t.InitiatorUserId)
                .Select(g => new { Actor = g.Key, Count = g.Count() })
                .Where(x => x.Count > _options.AdminMaxAdjustmentsPerHour)
                .ToListAsync(ct);

            var names = await NamesAsync(large.Where(l => l.InitiatorUserId.HasValue).Select(l => l.InitiatorUserId!.Value)
                .Concat(busy.Where(b => b.Actor.HasValue).Select(b => b.Actor!.Value)), ct);
            string Staff(int? id, string? component) => id.HasValue ? names.GetValueOrDefault(id.Value, $"#{id}") : component ?? "the game server";

            var drafts = new List<CurrencyAlertDraft>();
            foreach (var entry in large)
            {
                drafts.Add(new CurrencyAlertDraft(CurrencyAlertRules.Admin, CurrencyAlertSeverity.High,
                    Truncate($"{Staff(entry.InitiatorUserId, entry.InitiatorComponent)} changed a balance by {entry.Amount:+#,0;-#,0} {entry.Currency.ToString().ToLowerInvariant()} in one adjustment (TX {entry.PublicId})."),
                    $"R6:tx:{entry.TransactionId}:{entry.Currency}", CurrencyAlertRules.Forever)
                {
                    UserId = entry.UserId,
                    TransactionId = entry.TransactionId,
                    Details = new
                    {
                        publicId = entry.PublicId,
                        reasonCode = entry.ReasonCode,
                        currency = entry.Currency.ToString(),
                        amount = entry.Amount,
                        staffUserId = entry.InitiatorUserId,
                        threshold = entry.Currency == Currency.Coins ? coins : gems
                    }
                });
            }
            foreach (var actor in busy)
            {
                drafts.Add(new CurrencyAlertDraft(CurrencyAlertRules.Admin, CurrencyAlertSeverity.High,
                    Truncate($"{Staff(actor.Actor, null)} made {actor.Count} balance adjustments within an hour (more than {_options.AdminMaxAdjustmentsPerHour})."),
                    $"R6:actor:{actor.Actor?.ToString() ?? "server"}", TimeSpan.FromHours(1))
                {
                    UserId = actor.Actor,
                    Details = new { staffUserId = actor.Actor, adjustmentsLastHour = actor.Count, threshold = _options.AdminMaxAdjustmentsPerHour }
                });
            }
            return drafts;
        }

        /// <summary>
        /// R7: what a reason code minted (credited to players from a system account, so not
        /// player transfers) of coins or gems in the last 24 h is more than MintRateMultiplier ×
        /// its daily mean over the MintRateBaselineDays before, and at least the noise floor. A
        /// reason with no history is not compared.
        /// </summary>
        public async Task<List<CurrencyAlertDraft>> MintRateAsync(DateTime now, CancellationToken ct = default)
        {
            var dayStart = now - TimeSpan.FromHours(24);
            var baselineStart = dayStart - TimeSpan.FromDays(_options.MintRateBaselineDays);
            var today = await MintedAsync(dayStart, now.AddSeconds(1), ct);
            if (today.Count == 0)
            {
                return new List<CurrencyAlertDraft>();
            }
            var baseline = await MintedAsync(baselineStart, dayStart, ct);

            var drafts = new List<CurrencyAlertDraft>();
            foreach (var ((reason, currency), minted) in today)
            {
                var floor = currency == Currency.Coins ? _options.MintRateMinCoinsPerDay : _options.MintRateMinGemsPerDay;
                var mean = baseline.GetValueOrDefault((reason, currency)) / (double)Math.Max(1, _options.MintRateBaselineDays);
                if (mean <= 0 || minted < floor || minted <= _options.MintRateMultiplier * mean)
                {
                    continue;
                }
                drafts.Add(new CurrencyAlertDraft(CurrencyAlertRules.MintRate, CurrencyAlertSeverity.Medium,
                    $"{reason} minted {minted:N0} {currency.ToString().ToLowerInvariant()} in 24 h, {minted / mean:0.#}× its daily mean of {mean:N0}.",
                    $"R7:{reason}:{currency}", TimeSpan.FromHours(24))
                {
                    Details = new { reasonCode = reason, currency = currency.ToString(), minted24h = minted, dailyMean = Math.Round(mean, 2), baselineDays = _options.MintRateBaselineDays }
                });
            }
            return drafts;
        }

        /// <summary>Minted per reason and currency in [from, to).</summary>
        private async Task<Dictionary<(string Reason, Currency Currency), long>> MintedAsync(DateTime from, DateTime to, CancellationToken ct)
        {
            var rows = await _context.CurrencyEntries.AsNoTracking()
                .Where(e => e.AccountKind == CurrencyAccountKind.User && e.Amount > 0 && e.Currency != Currency.Experience
                            && e.Transaction.Kind != CurrencyTransactionKind.Transfer
                            && e.Transaction.CreatedAt >= from && e.Transaction.CreatedAt < to)
                .GroupBy(e => new { e.Transaction.ReasonCode, e.Currency })
                .Select(g => new { g.Key.ReasonCode, g.Key.Currency, Sum = g.Sum(e => e.Amount) })
                .ToListAsync(ct);
            return rows.ToDictionary(r => (r.ReasonCode, r.Currency), r => r.Sum);
        }

        /// <summary>Most mismatches listed in one R1/R2 alert (the reconciliation page has all of them).</summary>
        public const int MaxMismatchesPerAlert = 50;

        /// <summary>
        /// R1 (balance columns vs. the ledger chain) and R2 (unbalanced transactions) from one
        /// reconciler run: at most one alert each, listing the mismatches. The key is a hash of the
        /// mismatch set, so the same unresolved set re-alerts once a day, and a new mismatch at
        /// once. R1 carries the kill switch for the coin/gem currencies involved.
        /// </summary>
        public static List<CurrencyAlertDraft> FromReconciliation(IReadOnlyList<CurrencyMismatchDto> mismatches)
        {
            var drafts = new List<CurrencyAlertDraft>();
            var chain = mismatches.Where(m => m.Kind != "UnbalancedTransaction").ToList();
            var unbalanced = mismatches.Where(m => m.Kind == "UnbalancedTransaction").ToList();

            if (chain.Count > 0)
            {
                var currencies = chain.Select(m => Enum.TryParse<Currency>(m.Currency, out var c) ? c : (Currency?)null)
                    .Where(c => c is Currency.Coins or Currency.Gems).Select(c => c!.Value).Distinct().OrderBy(c => c).ToList();
                var users = chain.Select(m => m.UserId).Distinct().ToList();
                var perCurrency = string.Join(", ", chain.GroupBy(m => m.Currency).Select(g => $"{g.Count()} {g.Key.ToLowerInvariant()}"));
                drafts.Add(new CurrencyAlertDraft(CurrencyAlertRules.Reconciliation, CurrencyAlertSeverity.Critical,
                    Truncate($"Reconciliation found {chain.Count} balance mismatch{(chain.Count == 1 ? "" : "es")} ({perCurrency}) for {users.Count} player{(users.Count == 1 ? "" : "s")}."),
                    "R1:" + Fingerprint(chain), TimeSpan.FromHours(24))
                {
                    UserId = users.Count == 1 ? users[0] : null,
                    TransactionId = chain.Select(m => m.TransactionId).FirstOrDefault(t => t.HasValue),
                    Details = new { total = chain.Count, users = users.Count, mismatches = chain.Take(MaxMismatchesPerAlert) },
                    DisableTransfersFor = currencies
                });
            }
            if (unbalanced.Count > 0)
            {
                drafts.Add(new CurrencyAlertDraft(CurrencyAlertRules.UnbalancedTransaction, CurrencyAlertSeverity.Critical,
                    $"Reconciliation found {unbalanced.Count} transaction leg set{(unbalanced.Count == 1 ? "" : "s")} that don't sum to zero.",
                    "R2:" + Fingerprint(unbalanced), TimeSpan.FromHours(24))
                {
                    TransactionId = unbalanced.Count == 1 ? unbalanced[0].TransactionId : null,
                    Details = new { total = unbalanced.Count, mismatches = unbalanced.Take(MaxMismatchesPerAlert) }
                });
            }
            return drafts;
        }

        private static string Fingerprint(IEnumerable<CurrencyMismatchDto> mismatches)
        {
            var canonical = string.Join(";", mismatches
                .Select(m => $"{m.Kind}|{m.UserId}|{m.Currency}|{m.Expected}|{m.Actual}|{m.EntryId}|{m.TransactionId}")
                .OrderBy(s => s, StringComparer.Ordinal));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..24].ToLowerInvariant();
        }

        private async Task<Dictionary<int, string>> NamesAsync(IEnumerable<int> ids, CancellationToken ct)
        {
            var list = ids.Distinct().ToList();
            if (list.Count == 0)
            {
                return new Dictionary<int, string>();
            }
            return await _context.Users.AsNoTracking()
                .Where(u => list.Contains(u.Id))
                .Select(u => new { u.Id, u.Username })
                .ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        }

        /// <summary>currency_alerts.Summary holds 300 characters.</summary>
        internal static string Truncate(string summary) => summary.Length <= 300 ? summary : summary[..297] + "...";
    }
}
