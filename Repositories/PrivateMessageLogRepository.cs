using System;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class PrivateMessageLogRepository : IPrivateMessageLogRepository
    {
        private readonly KnKDbContext _context;

        public PrivateMessageLogRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<int> AddRangeIgnoringDuplicatesAsync(IReadOnlyList<PrivateMessageLogEntry> entries)
        {
            try
            {
                return await AddNewAsync(entries);
            }
            catch (DbUpdateException)
            {
                // A retry of the same batch raced this one (the plugin timed out and re-sent) and
                // stored some ids first - the unique index refused them. Start over from what is
                // stored now; a second failure is a real error.
                _context.ChangeTracker.Clear();
                return await AddNewAsync(entries);
            }
        }

        private async Task<int> AddNewAsync(IReadOnlyList<PrivateMessageLogEntry> entries)
        {
            var ids = entries.Select(e => e.ClientMessageId).Distinct().ToList();
            var stored = await _context.PrivateMessageLogEntries
                .Where(e => ids.Contains(e.ClientMessageId))
                .Select(e => e.ClientMessageId)
                .ToListAsync();

            var seen = new HashSet<Guid>(stored);
            var fresh = entries.Where(e => seen.Add(e.ClientMessageId)).ToList();
            if (fresh.Count == 0)
            {
                return 0;
            }

            await _context.PrivateMessageLogEntries.AddRangeAsync(fresh);
            await _context.SaveChangesAsync();
            return fresh.Count;
        }

        public async Task<PagedResult<PrivateMessageLogEntry>> SearchAsync(int participantUserId, int? otherUserId,
            DateTime? from, DateTime? to, int pageNumber, int pageSize)
        {
            var queryable = _context.PrivateMessageLogEntries.AsNoTracking();

            queryable = otherUserId.HasValue
                ? queryable.Where(e =>
                    (e.SenderUserId == participantUserId && e.RecipientUserId == otherUserId.Value) ||
                    (e.RecipientUserId == participantUserId && e.SenderUserId == otherUserId.Value))
                : queryable.Where(e => e.SenderUserId == participantUserId || e.RecipientUserId == participantUserId);

            if (from.HasValue)
            {
                queryable = queryable.Where(e => e.SentAt >= from.Value);
            }

            if (to.HasValue)
            {
                queryable = queryable.Where(e => e.SentAt < to.Value);
            }

            queryable = queryable.OrderByDescending(e => e.SentAt).ThenByDescending(e => e.Id);

            var totalCount = await queryable.CountAsync();

            var items = await queryable
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<PrivateMessageLogEntry>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<int> DeleteOlderThanAsync(DateTime beforeDate)
        {
            return await _context.PrivateMessageLogEntries
                .Where(e => e.SentAt < beforeDate)
                .ExecuteDeleteAsync();
        }

        public async Task<Dictionary<string, int>> GetUserIdsByUuidAsync(IReadOnlyCollection<string> uuids)
        {
            if (uuids.Count == 0)
            {
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }

            var users = await _context.Users
                .Where(u => u.Uuid != null && uuids.Contains(u.Uuid))
                .Select(u => new { u.Id, u.Uuid })
                .ToListAsync();

            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var user in users)
            {
                result[user.Uuid!] = user.Id;
            }
            return result;
        }
    }
}
