using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// GET api/Admission/{uuid}?name= (KNG-214, DESIGN § 5.2): may this Mojang-verified player join?
    /// When not, <see cref="Code"/> is the claim code for the disconnect screen (only for
    /// <see cref="AdmissionReasons.NotAdmitted"/>).
    /// </summary>
    public class AdmissionResultDto
    {
        [JsonPropertyName("admitted")]
        public bool Admitted { get; set; }

        /// <summary>Null when admitted; otherwise one of <see cref="AdmissionReasons"/>.</summary>
        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        /// <summary>The claim code as shown on the disconnect screen, XXXX-XXXX.</summary>
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("expiresAt")]
        public DateTime? ExpiresAt { get; set; }
    }

    public static class AdmissionReasons
    {
        /// <summary>No admitted account for this UUID: the answer carries a claim code.</summary>
        public const string NotAdmitted = "NotAdmitted";

        /// <summary>The account is deactivated or deleted: no code.</summary>
        public const string Deactivated = "Deactivated";
    }

    public class ClaimCheckRequestDto
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }
    }

    /// <summary>POST api/Admission/claim/check, for /link before login.</summary>
    public class ClaimCheckResponseDto
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        /// <summary>The Mojang-verified name the claim code was made for; null when not valid.</summary>
        [JsonPropertyName("minecraftName")]
        public string? MinecraftName { get; set; }

        [JsonPropertyName("expiresAt")]
        public DateTime? ExpiresAt { get; set; }
    }
}
