using System;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    public interface IPrivateMessageLogRepository
    {
        /// <summary>
        /// Stores the entries whose ClientMessageId isn't stored yet (nor repeated earlier in the
        /// same list). Returns how many were stored; the rest are duplicates of a retried batch.
        /// </summary>
        Task<int> AddRangeIgnoringDuplicatesAsync(IReadOnlyList<PrivateMessageLogEntry> entries);

        /// <summary>
        /// Messages <paramref name="participantUserId"/> sent or received - only those with
        /// <paramref name="otherUserId"/> when given - with SentAt in [from, to), newest first.
        /// </summary>
        Task<PagedResult<PrivateMessageLogEntry>> SearchAsync(int participantUserId, int? otherUserId,
            DateTime? from, DateTime? to, int pageNumber, int pageSize);

        /// <summary>Deletes every entry with SentAt before <paramref name="beforeDate"/>; returns the count.</summary>
        Task<int> DeleteOlderThanAsync(DateTime beforeDate);

        /// <summary>User ids for the given Minecraft UUIDs (case-insensitive); unknown UUIDs are left out.</summary>
        Task<Dictionary<string, int>> GetUserIdsByUuidAsync(IReadOnlyCollection<string> uuids);
    }
}
