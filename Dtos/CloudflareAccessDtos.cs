using System;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>GET api/Invitations/access-sync (KNG-216): the Cloudflare Access sync's state for
    /// the line on /admin/invitations. Never carries emails or the token.</summary>
    public class CloudflareAccessSyncStatusDto
    {
        /// <summary>CloudflareAccess:SyncEnabled.</summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        /// <summary>Account, list and token are set.</summary>
        [JsonPropertyName("configured")]
        public bool Configured { get; set; }

        [JsonPropertyName("lastSuccessAt")]
        public DateTime? LastSuccessAt { get; set; }

        [JsonPropertyName("lastAttemptAt")]
        public DateTime? LastAttemptAt { get; set; }

        /// <summary>Null while the last run went through.</summary>
        [JsonPropertyName("lastError")]
        public string? LastError { get; set; }

        [JsonPropertyName("pendingAdds")]
        public int PendingAdds { get; set; }

        [JsonPropertyName("pendingRemoves")]
        public int PendingRemoves { get; set; }

        /// <summary>Entries in the API-owned list after the last run; null before the first.</summary>
        [JsonPropertyName("listSize")]
        public int? ListSize { get; set; }

        [JsonPropertyName("reconcileMinutes")]
        public int ReconcileMinutes { get; set; }
    }
}
