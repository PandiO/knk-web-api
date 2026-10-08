using System;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// DTO for link code response.
    /// Returns a newly generated link code with expiration time.
    /// </summary>
    public class LinkCodeResponseDto
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = null!;

        [JsonPropertyName("expiresAt")]
        public DateTime ExpiresAt { get; set; }

        [JsonPropertyName("formattedCode")]
        public string FormattedCode { get; set; } = null!;  // ABC-12XYZ format for display
    }

    /// <summary>
    /// DTO for requesting a new link code generation.
    /// </summary>
    public class LinkCodeRequestDto
    {
        [JsonPropertyName("userId")]
        public int UserId { get; set; }
    }

    /// <summary>
    /// DTO for validating a link code.
    /// Returns validation status and associated user information if valid.
    /// </summary>
    public class ValidateLinkCodeResponseDto
    {
        [JsonPropertyName("isValid")]
        public bool IsValid { get; set; }

        [JsonPropertyName("userId")]
        public int? UserId { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    /// <summary>
    /// DTO for checking if duplicate accounts exist for a given UUID/username.
    /// </summary>
    public class DuplicateCheckDto
    {
        [JsonPropertyName("uuid")]
        public string Uuid { get; set; } = null!;

        [JsonPropertyName("username")]
        public string Username { get; set; } = null!;
    }

    /// <summary>
    /// DTO for duplicate check response.
    /// Indicates if duplicate exists and provides conflicting account information.
    /// </summary>
    public class DuplicateCheckResponseDto
    {
        [JsonPropertyName("hasDuplicate")]
        public bool HasDuplicate { get; set; }

        [JsonPropertyName("conflictingUser")]
        public UserSummaryDto? ConflictingUser { get; set; }

        [JsonPropertyName("primaryUser")]
        public UserSummaryDto? PrimaryUser { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = null!;
    }

    /// <summary>
    /// DTO for requesting account merge.
    /// User chooses which account to keep (primary) and which to delete (secondary).
    /// </summary>
    public class AccountMergeDto
    {
        [JsonPropertyName("primaryUserId")]
        public int PrimaryUserId { get; set; }  // Account to keep

        [JsonPropertyName("secondaryUserId")]
        public int SecondaryUserId { get; set; }  // Account to delete (soft-delete)
    }

    /// <summary>
    /// DTO for generating a link code.
    /// Supports both web app (via JWT, no body) and Minecraft plugin (via userId in body, no auth).
    /// </summary>
    public class GenerateLinkCodeRequestDto
    {
        [JsonPropertyName("userId")]
        public int? UserId { get; set; }
    }

    /// <summary>
    /// DTO for linking an existing web app account to Minecraft using a link code.
    /// Used in the web-app-first flow where user already has email/password set.
    /// Requires the user to be authenticated (JWT token in Authorization header).
    /// </summary>
    public class LinkMinecraftAccountDto
    {
        [JsonPropertyName("linkCode")]
        public string LinkCode { get; set; } = null!;
    }
}

