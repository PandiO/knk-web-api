using System;
using System.Text.Json.Serialization;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// One private message as knk-plugin ships it - an element of
    /// POST api/private-message-log/batch (docs/specs/private-messages/DESIGN.md §3.2). The API
    /// resolves the user ids from the UUIDs; a null UUID means the console.
    /// </summary>
    public class CreatePrivateMessageLogEntryDto
    {
        [JsonPropertyName("clientMessageId")]
        public Guid ClientMessageId { get; set; }

        [JsonPropertyName("sentAt")]
        public DateTime SentAt { get; set; }

        [JsonPropertyName("senderUuid")]
        public string? SenderUuid { get; set; }

        [JsonPropertyName("senderName")]
        public string SenderName { get; set; } = null!;

        [JsonPropertyName("recipientUuid")]
        public string? RecipientUuid { get; set; }

        [JsonPropertyName("recipientName")]
        public string RecipientName { get; set; } = null!;

        [JsonPropertyName("content")]
        public string Content { get; set; } = null!;

        [JsonPropertyName("outcome")]
        public PrivateMessageOutcome Outcome { get; set; }

        [JsonPropertyName("viaReply")]
        public bool ViaReply { get; set; }
    }

    /// <summary>The answer to a batch: how many entries were stored, how many were already there.</summary>
    public class PrivateMessageLogBatchResultDto
    {
        [JsonPropertyName("accepted")]
        public int Accepted { get; set; }

        [JsonPropertyName("duplicates")]
        public int Duplicates { get; set; }
    }

    /// <summary>One logged private message - GET api/private-message-log.</summary>
    public class PrivateMessageLogEntryDto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("sentAt")]
        public DateTime SentAt { get; set; }

        [JsonPropertyName("senderUserId")]
        public int? SenderUserId { get; set; }

        [JsonPropertyName("senderName")]
        public string SenderName { get; set; } = null!;

        [JsonPropertyName("recipientUserId")]
        public int? RecipientUserId { get; set; }

        [JsonPropertyName("recipientName")]
        public string RecipientName { get; set; } = null!;

        [JsonPropertyName("content")]
        public string Content { get; set; } = null!;

        /// <summary>PrivateMessageOutcome name: Delivered, BlockedIgnored, BlockedRateLimited, BlockedFrozen.</summary>
        [JsonPropertyName("outcome")]
        public string Outcome { get; set; } = null!;

        [JsonPropertyName("viaReply")]
        public bool ViaReply { get; set; }
    }

    /// <summary>
    /// Filters of GET api/private-message-log: one player's messages, optionally only those
    /// with <see cref="OtherUserId"/> and within [From, To), newest first.
    /// </summary>
    public class PrivateMessageLogQueryDto
    {
        public int ParticipantUserId { get; set; }
        public int? OtherUserId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
}
