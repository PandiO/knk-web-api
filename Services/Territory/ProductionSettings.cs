using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Json;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Territory
{
    /// <summary>Reads the production settings (<c>Production:Enabled</c>, territory ranges) from the game settings row.</summary>
    public interface IProductionSettingsReader
    {
        Task<ProductionSettingsDto> GetAsync();
    }

    /// <summary>
    /// The production section of the game settings (G16, territory DESIGN § 1). Stored as JSON in
    /// <see cref="GameSettings.ProductionSettingsJson"/>; a missing row or column means the defaults (flag off, base
    /// range 100, factor 2, capital × 1.5).
    /// </summary>
    public static class ProductionSettings
    {
        public static ProductionSettingsDto Read(GameSettings? settings)
        {
            var dto = JsonColumn.Deserialize<ProductionSettingsDto>(settings?.ProductionSettingsJson) ?? new ProductionSettingsDto();
            dto.Territory ??= new TerritorySettingsDto();
            return dto;
        }

        /// <summary>Validates (throws <see cref="System.ArgumentException"/>) and returns a clean copy.</summary>
        public static ProductionSettingsDto Normalize(ProductionSettingsDto dto) => new()
        {
            Enabled = dto.Enabled,
            Territory = TerritoryRules.Validate(dto.Territory),
        };
    }

    public sealed class ProductionSettingsReader : IProductionSettingsReader
    {
        private readonly KnKDbContext _db;

        public ProductionSettingsReader(KnKDbContext db) => _db = db;

        public async Task<ProductionSettingsDto> GetAsync() =>
            ProductionSettings.Read(await _db.GameSettings.AsNoTracking().FirstOrDefaultAsync());
    }
}
