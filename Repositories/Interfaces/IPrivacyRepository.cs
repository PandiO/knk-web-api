using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    /// <summary>GDPR deletion requests and the erasure itself (KNG-34 link 6, DESIGN.md §F.14).</summary>
    public interface IPrivacyRepository
    {
        Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default);

        Task<List<PrivacyDeletionRequest>> GetRequestsAsync(PrivacyRequestStatus? status, CancellationToken ct = default);

        Task<PrivacyDeletionRequest?> GetRequestAsync(int id, CancellationToken ct = default);

        /// <summary>The user's newest request that is awaiting confirmation or scheduled (tracked).</summary>
        Task<PrivacyDeletionRequest?> GetOpenRequestOfUserAsync(int userId, CancellationToken ct = default);

        /// <summary>The request whose confirmation token hashes to <paramref name="tokenHash"/> (tracked).</summary>
        Task<PrivacyDeletionRequest?> GetRequestByTokenHashAsync(string tokenHash, CancellationToken ct = default);

        /// <summary>Scheduled requests whose grace period ended at or before <paramref name="now"/>, oldest first.</summary>
        Task<List<PrivacyDeletionRequest>> GetScheduledDueAsync(DateTime now, CancellationToken ct = default);

        /// <summary>Unconfirmed requests whose link expired (tracked).</summary>
        Task<List<PrivacyDeletionRequest>> GetExpiredConfirmationsAsync(DateTime now, CancellationToken ct = default);

        Task AddRequestAsync(PrivacyDeletionRequest request, CancellationToken ct = default);

        Task SaveChangesAsync(CancellationToken ct = default);

        Task<User?> GetUserAsync(int userId, CancellationToken ct = default);

        Task<Dictionary<int, string>> GetUsernamesAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default);

        /// <summary>Rows per erasure scope (table) that <see cref="EraseUserDataAsync"/> would delete for these users.</summary>
        Task<SortedDictionary<string, int>> CountUserDataAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default);

        /// <summary>Deletes every row in the §F.14 scope for these users; returns rows deleted per table.</summary>
        Task<SortedDictionary<string, int>> EraseUserDataAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default);

        /// <summary>Pseudonymizes the users rows (§F.14); returns how many rows changed.</summary>
        Task<int> PseudonymizeUsersAsync(IReadOnlyCollection<int> userIds, DateTime now, string reason, CancellationToken ct = default);

        // ---- realtime S5 (PandiO/knk-workspace#184): the erasure feed ----

        /// <summary>
        /// Adds one feed row per user (in the order given) with the UUID the account has now; call it before
        /// <see cref="PseudonymizeUsersAsync"/>, which clears the UUID. Not saved: the next save writes the rows (in the
        /// erasure's transaction) and gives them their ids.
        /// </summary>
        Task<List<PrivacyErasureEvent>> AddErasureEventsAsync(IReadOnlyCollection<int> userIds, int requestId, DateTime now,
            CancellationToken ct = default);

        /// <summary>Feed rows with an id above <paramref name="sinceId"/>, oldest first, at most <paramref name="limit"/>.</summary>
        Task<List<PrivacyErasureEvent>> GetErasureEventsSinceAsync(long sinceId, int limit, CancellationToken ct = default);

        /// <summary>Which of these UUIDs an account has now (a returning player's fresh account).</summary>
        Task<HashSet<string>> GetUuidsInUseAsync(IReadOnlyCollection<string> uuids, CancellationToken ct = default);

        /// <summary>The newest feed id, 0 when the feed is empty.</summary>
        Task<long> GetLatestErasureEventIdAsync(CancellationToken ct = default);

        /// <summary>Clears the former UUID of feed rows erased before <paramref name="cutoff"/>; returns how many.</summary>
        Task<int> ClearErasureUuidsBeforeAsync(DateTime cutoff, DateTime now, CancellationToken ct = default);
    }
}
