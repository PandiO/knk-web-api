using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// A pending in-game moment for one player that the plugin has not shown yet - currently only
    /// a consolidated title change from AdjustBalancesAsync. Exists because a balance adjustment
    /// made through the web app returns its TitleChangeResultDto to the browser only; without this
    /// queue the Minecraft server never learned a promotion happened, so the player never saw the
    /// PromotionEffects sound/particles/message (the /knk user command only worked because the
    /// plugin itself was the caller and read the result straight off the HTTP response).
    /// </summary>
    public class PlayerNotificationDto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        /// <summary>Minecraft UUID, null for a web-app-only account that never joined.</summary>
        [JsonPropertyName("uuid")]
        public string? Uuid { get; set; }

        [JsonPropertyName("username")]
        public string Username { get; set; } = null!;

        /// <summary>See PlayerNotificationTypes.</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = null!;

        /// <summary>Set when Type is TitleChanged.</summary>
        [JsonPropertyName("titleChange")]
        public TitleChangeResultDto? TitleChange { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }
    }

    public static class PlayerNotificationTypes
    {
        public const string TitleChanged = "TitleChanged";

        /// <summary>
        /// A user's group memberships changed (web app, API, or a temporary rank expiring) - the
        /// plugin re-reads the player so their rank shows in chat and the tab list straight away.
        /// No payload.
        /// </summary>
        public const string RankChanged = "RankChanged";
    }

    public class AcknowledgePlayerNotificationsDto
    {
        [JsonPropertyName("ids")]
        public List<long> Ids { get; set; } = new();
    }
}
