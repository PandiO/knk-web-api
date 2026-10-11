using knkwebapi_v2.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Per-account login lockout (closed-alpha hardening WP6.2): after Security:Lockout:MaxFailures
    /// failed logins within WindowMinutes the account is locked for LockMinutes, and logins aren't
    /// even checked while it is. A success resets the count. Keyed per account (user id), or per
    /// identifier when no account matches, so unknown names behave the same as known ones.
    /// In memory: one API instance, cleared by a restart.
    /// </summary>
    public interface ILoginAttemptLimiter
    {
        bool IsLocked(string key, out TimeSpan remaining);

        /// <summary>Counts a failure; true when this failure locked the account.</summary>
        bool RecordFailure(string key);

        void Reset(string key);
    }

    public class LoginAttemptLimiter : ILoginAttemptLimiter
    {
        private sealed class Counter
        {
            public int Failures;
        }

        private readonly IMemoryCache _cache;
        private readonly LockoutSettings _settings;
        private readonly object _gate = new();

        public LoginAttemptLimiter(IMemoryCache cache, IOptions<SecuritySettings> settings)
        {
            _cache = cache;
            _settings = settings.Value.Lockout;
        }

        private static string FailKey(string key) => $"knk:login-fail:{key}";
        private static string LockKey(string key) => $"knk:login-lock:{key}";

        public bool IsLocked(string key, out TimeSpan remaining)
        {
            if (_cache.TryGetValue(LockKey(key), out DateTime lockedUntil) && lockedUntil > DateTime.UtcNow)
            {
                remaining = lockedUntil - DateTime.UtcNow;
                return true;
            }
            remaining = TimeSpan.Zero;
            return false;
        }

        public bool RecordFailure(string key)
        {
            lock (_gate)
            {
                var counter = _cache.GetOrCreate(FailKey(key), entry =>
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
                _cache.Set(LockKey(key), DateTime.UtcNow.Add(lockFor), lockFor);
                _cache.Remove(FailKey(key));
                return true;
            }
        }

        public void Reset(string key)
        {
            _cache.Remove(FailKey(key));
            _cache.Remove(LockKey(key));
        }

        /// <summary>"Too many attempts. Try again in N minutes." - shown to players verbatim.</summary>
        public static string LockedMessage(TimeSpan remaining)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
            return $"Too many attempts. Try again in {minutes} {(minutes == 1 ? "minute" : "minutes")}.";
        }
    }
}
