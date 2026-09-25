using System;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// The audit log retention configuration (docs/specs/user-management/DESIGN.md §7 item 3).
    /// Singleton, admin-editable via raw GET/PUT — mirrors SalaryConfigurationController's shape
    /// (no web-app UI/FormConfiguration authored, matching that precedent for admin-only config).
    /// </summary>
    public class AuditLogRetentionConfigurationDto
    {
        [JsonPropertyName("retentionDays")]
        public int RetentionDays { get; set; } = 180;

        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// DTO for updating the audit log retention configuration.
    /// </summary>
    public class UpdateAuditLogRetentionConfigurationDto
    {
        [JsonPropertyName("retentionDays")]
        public int RetentionDays { get; set; } = 180;
    }
}
