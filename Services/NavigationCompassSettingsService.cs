using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// The navigation compass settings (KNG-234): one row, created by the first save. The game server reads
    /// it with GET api/navigation-settings/compass and binds the compass behaviour to any item whose
    /// material and display name match.
    /// </summary>
    public class NavigationCompassSettingsService : INavigationCompassSettingsService
    {
        // A namespaced Minecraft material key: minecraft:recovery_compass, COMPASS.
        private static readonly Regex MaterialPattern = new(@"^[A-Za-z0-9_]+(:[A-Za-z0-9_./-]+)?$", RegexOptions.Compiled);

        private readonly KnKDbContext _db;

        public NavigationCompassSettingsService(KnKDbContext db)
        {
            _db = db;
        }

        public async Task<NavigationCompassSettingsDto> GetAsync()
        {
            var row = await _db.NavigationCompassSettingsRows.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == NavigationCompassSettings.SingletonId);
            return row == null ? ToDto(new NavigationCompassSettings(), stored: false) : ToDto(row, stored: true);
        }

        public async Task<NavigationCompassSettingsDto> UpdateAsync(UpdateNavigationCompassSettingsDto dto)
        {
            if (dto == null) throw new ArgumentException("A request body is required.", nameof(dto));

            string? material = null;
            if (dto.Material != null)
            {
                material = dto.Material.Trim();
                if (material.Length == 0 || material.Length > NavigationCompassSettings.MaxMaterialLength || !MaterialPattern.IsMatch(material))
                    throw new ArgumentException("material must be a Minecraft material such as COMPASS or minecraft:recovery_compass.");
            }
            string? displayName = null;
            if (dto.DisplayName != null)
            {
                displayName = dto.DisplayName.Trim();
                if (displayName.Length > NavigationCompassSettings.MaxDisplayNameLength)
                    throw new ArgumentException($"displayName can be at most {NavigationCompassSettings.MaxDisplayNameLength} characters.");
            }
            NavigationCompassNameMatch? nameMatch = null;
            if (dto.NameMatch != null)
            {
                nameMatch = ParseNameMatch(dto.NameMatch)
                    ?? throw new ArgumentException("nameMatch must be Exact or Contains.");
            }
            string? targets = dto.GeneralTargets == null ? null : NormalizeTargets(dto.GeneralTargets);

            var row = await _db.NavigationCompassSettingsRows.FirstOrDefaultAsync(r => r.Id == NavigationCompassSettings.SingletonId);
            if (row == null)
            {
                row = new NavigationCompassSettings();
                _db.NavigationCompassSettingsRows.Add(row);
            }
            if (dto.Enabled.HasValue) row.Enabled = dto.Enabled.Value;
            if (material != null) row.Material = material;
            if (displayName != null) row.DisplayName = displayName;
            if (nameMatch.HasValue) row.NameMatch = nameMatch.Value;
            if (dto.SiegeEnabled.HasValue) row.SiegeEnabled = dto.SiegeEnabled.Value;
            if (dto.GeneralEnabled.HasValue) row.GeneralEnabled = dto.GeneralEnabled.Value;
            if (targets != null) row.GeneralTargets = targets;
            row.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return ToDto(row, stored: true);
        }

        /// <summary>"Exact" / "Contains" in any case; null for anything else.</summary>
        public static NavigationCompassNameMatch? ParseNameMatch(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            foreach (var match in Enum.GetValues<NavigationCompassNameMatch>())
            {
                if (match.ToString().Equals(value.Trim(), StringComparison.OrdinalIgnoreCase)) return match;
            }
            return null;
        }

        /// <summary>One destination per line, trimmed, blank lines dropped; bounded so a form cannot store a novel.</summary>
        public static string NormalizeTargets(string text)
        {
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();
            if (lines.Count > NavigationCompassSettings.MaxTargets)
                throw new ArgumentException($"At most {NavigationCompassSettings.MaxTargets} destinations are allowed.");
            var tooLong = lines.FirstOrDefault(l => l.Length > NavigationCompassSettings.MaxTargetLength);
            if (tooLong != null)
                throw new ArgumentException($"A destination can be at most {NavigationCompassSettings.MaxTargetLength} characters: '{tooLong[..20]}...'.");
            return string.Join("\n", lines);
        }

        private static NavigationCompassSettingsDto ToDto(NavigationCompassSettings row, bool stored) => new()
        {
            Enabled = row.Enabled,
            Material = row.Material,
            DisplayName = row.DisplayName,
            NameMatch = row.NameMatch.ToString(),
            SiegeEnabled = row.SiegeEnabled,
            GeneralEnabled = row.GeneralEnabled,
            GeneralTargets = row.GeneralTargets,
            UpdatedAt = stored ? row.UpdatedAt : null
        };
    }
}
