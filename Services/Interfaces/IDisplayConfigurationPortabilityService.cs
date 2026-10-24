using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// DisplayConfiguration export/import as portable "knk-displayconfig/1" files, and the bundled seed files in
    /// Data/DisplayConfigurationSeeds/ (spec: that folder's README.md). The display counterpart of
    /// IFormConfigurationPortabilityService (KNG-138).
    /// </summary>
    public interface IDisplayConfigurationPortabilityService
    {
        /// <summary>The portable file for a configuration, or null when it doesn't exist.</summary>
        Task<PortableDisplayConfigurationDto?> ExportAsync(int id);

        /// <summary>
        /// Imports (or with dryRun only checks) a portable file. Never overwrites: an existing configurationGuid
        /// writes nothing. The new configuration becomes the entity's default only when it has none, or when
        /// makeDefault is asked (the old default is unset in the same transaction). It is published unless
        /// asDraft is asked.
        /// </summary>
        Task<DisplayConfigurationImportResultDto> ImportAsync(PortableDisplayConfigurationDto file, bool dryRun, bool makeDefault, bool asDraft);

        /// <summary>Parses the JSON text of a portable file, then imports it as ImportAsync does.</summary>
        Task<DisplayConfigurationImportResultDto> ImportJsonAsync(string json, bool dryRun, bool makeDefault, bool asDraft);

        Task<IReadOnlyList<DisplayConfigurationSeedDto>> ListSeedsAsync();

        /// <summary>Null when no bundled seed has that name.</summary>
        Task<DisplayConfigurationImportResultDto?> ImportSeedAsync(string name, bool dryRun, bool makeDefault, bool asDraft);
    }
}
