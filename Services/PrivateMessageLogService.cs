using System.Text.Json;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    public class PrivateMessageLogService : IPrivateMessageLogService
    {
        /// <summary>Largest batch the plugin may send (it sends up to 50 at a time).</summary>
        public const int MaxBatchSize = 200;

        public const int MaxPageSize = 100;

        /// <summary>
        /// How far ahead of the API's clock a plugin timestamp may be before it is pulled back to
        /// now - a clock that runs ahead would otherwise keep rows past their retention.
        /// </summary>
        private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

        private readonly IPrivateMessageLogRepository _repo;
        private readonly IAuditLogService _auditLog;
        private readonly ILogger<PrivateMessageLogService> _logger;

        public PrivateMessageLogService(IPrivateMessageLogRepository repo, IAuditLogService auditLog,
            ILogger<PrivateMessageLogService> logger)
        {
            _repo = repo;
            _auditLog = auditLog;
            _logger = logger;
        }

        public async Task<PrivateMessageLogBatchResultDto> AddBatchAsync(IReadOnlyList<CreatePrivateMessageLogEntryDto> entries)
        {
            if (entries == null) throw new ArgumentException("A batch is required.", nameof(entries));
            if (entries.Count > MaxBatchSize)
                throw new ArgumentException($"A batch holds at most {MaxBatchSize} entries (got {entries.Count}).", nameof(entries));
            if (entries.Count == 0) return new PrivateMessageLogBatchResultDto();

            var now = DateTime.UtcNow;
            var uuids = new List<string?>();
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i] ?? throw new ArgumentException($"Entry {i} is empty.", nameof(entries));
                if (entry.ClientMessageId == Guid.Empty)
                    throw new ArgumentException($"Entry {i} has no clientMessageId.", nameof(entries));
                if (entry.SentAt == default)
                    throw new ArgumentException($"Entry {i} has no sentAt.", nameof(entries));
                if (string.IsNullOrWhiteSpace(entry.SenderName) || string.IsNullOrWhiteSpace(entry.RecipientName))
                    throw new ArgumentException($"Entry {i} needs senderName and recipientName.", nameof(entries));
                if (entry.Content == null)
                    throw new ArgumentException($"Entry {i} has no content.", nameof(entries));
                if (!Enum.IsDefined(entry.Outcome))
                    throw new ArgumentException($"Entry {i} has an unknown outcome.", nameof(entries));
                uuids.Add(NormalizeUuid(entry.SenderUuid, i));
                uuids.Add(NormalizeUuid(entry.RecipientUuid, i));
            }

            var userIds = await _repo.GetUserIdsByUuidAsync(uuids.OfType<string>().Distinct().ToList());
            int? UserIdOf(string? uuid) => uuid != null && userIds.TryGetValue(uuid, out var id) ? id : null;

            var rows = entries.Select(entry =>
            {
                var sentAt = entry.SentAt.Kind switch
                {
                    DateTimeKind.Local => entry.SentAt.ToUniversalTime(),
                    DateTimeKind.Unspecified => DateTime.SpecifyKind(entry.SentAt, DateTimeKind.Utc),
                    _ => entry.SentAt
                };
                if (sentAt > now + MaxClockSkew) sentAt = now;

                return new PrivateMessageLogEntry
                {
                    ClientMessageId = entry.ClientMessageId,
                    SentAt = sentAt,
                    SenderUserId = UserIdOf(NormalizeUuid(entry.SenderUuid, 0)),
                    SenderName = Cap(entry.SenderName.Trim(), PrivateMessageLogEntry.NameMaxLength),
                    RecipientUserId = UserIdOf(NormalizeUuid(entry.RecipientUuid, 0)),
                    RecipientName = Cap(entry.RecipientName.Trim(), PrivateMessageLogEntry.NameMaxLength),
                    Content = Cap(entry.Content, PrivateMessageLogEntry.ContentMaxLength),
                    Outcome = entry.Outcome,
                    ViaReply = entry.ViaReply
                };
            }).ToList();

            var accepted = await _repo.AddRangeIgnoringDuplicatesAsync(rows);
            var result = new PrivateMessageLogBatchResultDto { Accepted = accepted, Duplicates = rows.Count - accepted };
            _logger.LogInformation("Private message log batch: {Accepted} accepted, {Duplicates} duplicates",
                result.Accepted, result.Duplicates);
            return result;
        }

        public async Task<PagedResultDto<PrivateMessageLogEntryDto>> SearchAsync(PrivateMessageLogQueryDto query, int? viewerUserId)
        {
            if (query == null) throw new ArgumentException("A query is required.", nameof(query));
            if (query.ParticipantUserId <= 0)
                throw new ArgumentException("participantUserId is required.", nameof(query));

            var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
            var pageSize = query.PageSize < 1 ? 20 : Math.Min(query.PageSize, MaxPageSize);
            var from = ToUtc(query.From);
            var to = ToUtc(query.To);

            var page = await _repo.SearchAsync(query.ParticipantUserId, query.OtherUserId, from, to, pageNumber, pageSize);

            // Reading someone's private messages is itself on the record (DESIGN.md §3.2).
            await _auditLog.RecordAsync(viewerUserId, query.ParticipantUserId, AuditAction.PrivateMessagesViewed,
                JsonSerializer.Serialize(new
                {
                    otherUserId = query.OtherUserId,
                    from,
                    to,
                    pageNumber,
                    pageSize,
                    shown = page.Items.Count
                }));

            return new PagedResultDto<PrivateMessageLogEntryDto>
            {
                Items = page.Items.Select(ToDto).ToList(),
                TotalCount = page.TotalCount,
                PageNumber = page.PageNumber,
                PageSize = page.PageSize
            };
        }

        /// <summary>Lower-case "D" form, or null for the console; throws for anything that isn't a UUID.</summary>
        private static string? NormalizeUuid(string? uuid, int index)
        {
            if (string.IsNullOrWhiteSpace(uuid)) return null;
            if (!Guid.TryParse(uuid, out var parsed))
                throw new ArgumentException($"Entry {index} has an invalid UUID.", "entries");
            return parsed.ToString("D");
        }

        private static string Cap(string value, int max) => value.Length <= max ? value : value[..max];

        private static DateTime? ToUtc(DateTime? value) => value switch
        {
            null => null,
            { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
            { Kind: DateTimeKind.Unspecified } v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
            var v => v
        };

        private static PrivateMessageLogEntryDto ToDto(PrivateMessageLogEntry entry) => new()
        {
            Id = entry.Id,
            SentAt = DateTime.SpecifyKind(entry.SentAt, DateTimeKind.Utc),
            SenderUserId = entry.SenderUserId,
            SenderName = entry.SenderName,
            RecipientUserId = entry.RecipientUserId,
            RecipientName = entry.RecipientName,
            Content = entry.Content,
            Outcome = entry.Outcome.ToString(),
            ViaReply = entry.ViaReply
        };
    }
}
