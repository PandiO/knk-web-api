using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// KNG-138: FormConfiguration export/import as portable "knk-formconfig/1" files, and the bundled
    /// seed files in Data/FormConfigurationSeeds/.
    /// </summary>
    public interface IFormConfigurationPortabilityService
    {
        /// <summary>The portable file for a configuration, or null when it doesn't exist.</summary>
        Task<PortableFormConfigurationDto?> ExportAsync(int id);

        /// <summary>
        /// Imports (or with dryRun only checks) a portable file. Never overwrites: an existing
        /// configurationGuid writes nothing. The new configuration becomes the entity's default only
        /// when it has none, or when makeDefault is asked (the old default is unset in the same
        /// transaction).
        /// </summary>
        Task<FormConfigurationImportResultDto> ImportAsync(PortableFormConfigurationDto file, bool dryRun, bool makeDefault);

        /// <summary>Parses the JSON text of a portable file, then imports it as ImportAsync does.</summary>
        Task<FormConfigurationImportResultDto> ImportJsonAsync(string json, bool dryRun, bool makeDefault);

        Task<IReadOnlyList<FormConfigurationSeedDto>> ListSeedsAsync();

        /// <summary>Null when no bundled seed has that name.</summary>
        Task<FormConfigurationImportResultDto?> ImportSeedAsync(string name, bool dryRun, bool makeDefault);
    }
}
