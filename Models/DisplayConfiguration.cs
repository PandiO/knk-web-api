using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace knkwebapi_v2.Models
{
    /// <summary>
    /// Represents a display configuration template for rendering entity data.
    /// Similar to FormConfiguration but for read-only display purposes.
    /// Multiple configurations can exist per entity type, with one marked as default.
    /// 
    /// DRAFT MODE:
    /// - IsDraft = true: Configuration can be incomplete, validation is relaxed
    /// - IsDraft = false: Full validation required, configuration is production-ready
    /// This allows administrators to save work in progress without completing everything.
    /// </summary>
    public class DisplayConfiguration
    {
        public int Id { get; set; }
        
        public Guid ConfigurationGuid { get; set; } = Guid.NewGuid();
        
        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = null!;
        
        [Required]
        [MaxLength(100)]
        public string EntityTypeName { get; set; } = null!;
        
        public bool IsDefault { get; set; } = false;
        
        [MaxLength(500)]
        public string? Description { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime? UpdatedAt { get; set; }
        
        /// <summary>
        /// JSON array storing ordered Section GUIDs: ["guid-1", "guid-2", ...].
        /// Allows flexible section reordering without database updates.
        /// </summary>
        [Required]
        public string SectionOrderJson { get; set; } = "[]";
        
        /// <summary>
        /// Draft mode allows incomplete configurations to be saved.
        /// When false, full validation is enforced.
        /// </summary>
        public bool IsDraft { get; set; } = true;

        /// <summary>
        /// PandiO/knk-workspace#71: the builder's saved working copy (a DisplayConfigurationDto as JSON), or null when
        /// the builder has nothing unpublished. The rows stay the version detail pages read; Publish applies
        /// this copy to them and clears it.
        /// </summary>
        public string? DraftJson { get; set; }

        /// <summary>#71: when DraftJson was last saved.</summary>
        public DateTime? DraftSavedAt { get; set; }

        /// <summary>#71: when it was last published (null for rows published before #71).</summary>
        public DateTime? PublishedAt { get; set; }
        
        // Navigation properties
        public List<DisplaySection> Sections { get; set; } = new();
    }
}
