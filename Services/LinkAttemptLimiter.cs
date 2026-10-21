using knkwebapi_v2.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Per-account limit on failed link and claim codes (KNG-208, invitations DESIGN § 5.5): after
    /// Security:LinkAttempts:MaxFailures wrong codes within WindowMinutes the account is blocked for
    /// LockMinutes and codes aren't even looked up; a success resets the count. Keyed by the
    /// logged-in user's id. In memory, like <see cref="LoginAttemptLimiter"/>: one API instance,
    /// cleared by a restart. The per-IP "lookup" rate limit still applies on top.
    /// </summary>
    public interface ILinkAttemptLimiter
    {
        bool IsLocked(int userId, out TimeSpan remaining);

        /// <summary>Counts a failure; true when this failure blocked the account.</summary>
        bool RecordFailure(int userId);

        void Reset(int userId);
    }

    public class LinkAttemptLimiter : ILinkAttemptLimiter
    {
        private sealed class Counter
        {
            public int Failures;
        }

        private readonly IMemoryCache _cache;
        private readonly LockoutSettings _settings;
        private readonly object _gate = new();

        public LinkAttemptLimiter(IMemoryCache cache, IOptions<SecuritySettings> settings)
        {
            _cache = cache;
            _settings = settings.Value.LinkAttempts;
        }

        private static string FailKey(int userId) => $"knk:link-fail:{userId}";
        private static string LockKey(int userId) => $"knk:link-lock:{userId}";

        public bool IsLocked(int userId, out TimeSpan remaining)
        {
            if (_cache.TryGetValue(LockKey(userId), out DateTime lockedUntil) && lockedUntil > DateTime.UtcNow)
            {
                remaining = lockedUntil - DateTime.UtcNow;
                return true;
            }
            remaining = TimeSpan.Zero;
            return false;
        }

        public bool RecordFailure(int userId)
        {
            lock (_gate)
            {
                var counter = _cache.GetOrCreate(FailKey(userId), entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(Math.Max(1, _settings.WindowMinutes));
                    return new Counter();
                })!;
                counter.Failures++;
                if (counter.Failures < Math.Max(1, _settings.MaxFailures))
                {
                    return false;
                }

                var lockFor = TimeSpan.FromMinutes(Math.Max(1, _settings.LockMinutes));
                _cache.Set(LockKey(userId), DateTime.UtcNow.Add(lockFor), lockFor);
                _cache.Remove(FailKey(userId));
                return true;
            }
        }

        public void Reset(int userId)
        {
            _cache.Remove(FailKey(userId));
            _cache.Remove(LockKey(userId));
        }
    }

    /// <summary>
    /// link-minecraft-account refused the request before looking at the code: the account is
    /// blocked by <see cref="ILinkAttemptLimiter"/>. Controllers answer 429 TooManyAttempts.
    /// </summary>
    public sealed class LinkAttemptsExceededException : Exception
    {
        public TimeSpan Remaining { get; }

        public LinkAttemptsExceededException(TimeSpan remaining)
            : base(LoginAttemptLimiter.LockedMessage(remaining))
        {
            Remaining = remaining;
        }
    }

    /// <summary>
    /// link-minecraft-account refused a valid code because of what it would do (invitations DESIGN
    /// § 5.4): <see cref="AlreadyRegistered"/> when the Minecraft account already has a web login,
    /// <see cref="AlreadyLinked"/> when the caller is already linked to another Minecraft account.
    /// Controllers answer 409 with <see cref="Code"/> as the error code.
    /// </summary>
    public sealed class LinkConflictException : Exception
    {
        public const string AlreadyRegistered = "AlreadyRegistered";
        public const string AlreadyLinked = "AlreadyLinked";

        public string Code { get; }

        public LinkConflictException(string code, string message) : base(message)
        {
            Code = code;
        }
    }
}
