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
            dto.Engine ??= new ProductionEngineSettingsDto();
            return dto;
        }

        /// <summary>Validates (throws <see cref="System.ArgumentException"/>) and returns a clean copy.</summary>
        public static ProductionSettingsDto Normalize(ProductionSettingsDto dto) => new()
        {
            Enabled = dto.Enabled,
            Territory = TerritoryRules.Validate(dto.Territory),
            Engine = ValidateEngine(dto.Engine),
        };

        private static ProductionEngineSettingsDto ValidateEngine(ProductionEngineSettingsDto? e)
        {
            e ??= new ProductionEngineSettingsDto();
            if (e.BaseCycleMinutes is < ProductionEngineSettingsDto.MinCycleMinutes or > ProductionEngineSettingsDto.MaxCycleMinutes)
                throw new System.ArgumentException($"production.engine.baseCycleMinutes must be {ProductionEngineSettingsDto.MinCycleMinutes} to {ProductionEngineSettingsDto.MaxCycleMinutes}.");
            if (e.CatchUpCapHours is < ProductionEngineSettingsDto.MinCatchUpHours or > ProductionEngineSettingsDto.MaxCatchUpHours)
                throw new System.ArgumentException($"production.engine.catchUpCapHours must be {ProductionEngineSettingsDto.MinCatchUpHours} to {ProductionEngineSettingsDto.MaxCatchUpHours}.");
            if (e.WeatherDayMinutes is < ProductionEngineSettingsDto.MinWeatherDayMinutes or > ProductionEngineSettingsDto.MaxWeatherDayMinutes)
                throw new System.ArgumentException($"production.engine.weatherDayMinutes must be {ProductionEngineSettingsDto.MinWeatherDayMinutes} to {ProductionEngineSettingsDto.MaxWeatherDayMinutes}.");
            return new ProductionEngineSettingsDto
            {
                BaseCycleMinutes = e.BaseCycleMinutes,
                CatchUpCapHours = e.CatchUpCapHours,
                WeatherDayMinutes = e.WeatherDayMinutes,
            };
        }
    }

    public sealed class ProductionSettingsReader : IProductionSettingsReader
    {
        private readonly KnKDbContext _db;

        public ProductionSettingsReader(KnKDbContext db) => _db = db;

        public async Task<ProductionSettingsDto> GetAsync() =>
            ProductionSettings.Read(await _db.GameSettings.AsNoTracking().FirstOrDefaultAsync());
    }
}
