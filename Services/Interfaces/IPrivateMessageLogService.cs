using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// The server-side private message log (KNG-18 Phase 3, docs/specs/private-messages/DESIGN.md
    /// §3.1/§3.2): the game server appends in batches, staff read one player's messages, and every
    /// read is itself audited (<c>AuditAction.PrivateMessagesViewed</c>).
    /// </summary>
    public interface IPrivateMessageLogService
    {
        /// <summary>
        /// Stores a batch of at most <c>PrivateMessageLogService.MaxBatchSize</c> entries, skipping
        /// ClientMessageIds already stored, so a re-sent batch is harmless. Throws
        /// ArgumentException for an oversized or malformed batch (nothing is stored then).
        /// </summary>
        Task<PrivateMessageLogBatchResultDto> AddBatchAsync(IReadOnlyList<CreatePrivateMessageLogEntryDto> entries);

        /// <summary>
        /// One player's messages, newest first, and an audit entry saying <paramref name="viewerUserId"/>
        /// (null = the game server / system) read them. Throws ArgumentException for a bad query.
        /// </summary>
        Task<PagedResultDto<PrivateMessageLogEntryDto>> SearchAsync(PrivateMessageLogQueryDto query, int? viewerUserId);
    }
}
