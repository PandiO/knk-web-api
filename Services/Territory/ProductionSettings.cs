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
            dto.Transport ??= new ProductionTransportSettingsDto();
            return dto;
        }

        /// <summary>Validates (throws <see cref="System.ArgumentException"/>) and returns a clean copy. A missing transport
        /// section keeps <paramref name="stored"/>'s (P5: clients that don't send it yet don't reset it).</summary>
        public static ProductionSettingsDto Normalize(ProductionSettingsDto dto, ProductionSettingsDto? stored = null) => new()
        {
            Enabled = dto.Enabled,
            Territory = TerritoryRules.Validate(dto.Territory),
            Engine = ValidateEngine(dto.Engine),
            Transport = ValidateTransport(dto.Transport ?? stored?.Transport),
        };

        private static ProductionTransportSettingsDto ValidateTransport(ProductionTransportSettingsDto? t)
        {
            t ??= new ProductionTransportSettingsDto();
            const string p = "production.transport.";
            if (t.WalkingSpeedBlocksPerSecond is < ProductionTransportSettingsDto.MinSpeed or > ProductionTransportSettingsDto.MaxSpeed)
                throw new System.ArgumentException($"{p}walkingSpeedBlocksPerSecond must be {ProductionTransportSettingsDto.MinSpeed} to {ProductionTransportSettingsDto.MaxSpeed}.");
            if (t.DefaultCarrierCapacity is < 1 or > ProductionTransportSettingsDto.MaxUnits)
                throw new System.ArgumentException($"{p}defaultCarrierCapacity must be 1 to {ProductionTransportSettingsDto.MaxUnits}.");
            if (t.DefaultThresholdPercent is < 1 or > 100)
                throw new System.ArgumentException($"{p}defaultThresholdPercent must be 1 to 100.");
            if (t.DefaultThresholdUnits is < 1 or > ProductionTransportSettingsDto.MaxUnits)
                throw new System.ArgumentException($"{p}defaultThresholdUnits must be 1 to {ProductionTransportSettingsDto.MaxUnits}.");
            if (t.UnmeasuredRouteFactor is < ProductionTransportSettingsDto.MinRouteFactor or > ProductionTransportSettingsDto.MaxRouteFactor)
                throw new System.ArgumentException($"{p}unmeasuredRouteFactor must be {ProductionTransportSettingsDto.MinRouteFactor} to {ProductionTransportSettingsDto.MaxRouteFactor}.");
            if (t.FallbackDistanceBlocks is < 1 or > ProductionTransportSettingsDto.MaxUnits)
                throw new System.ArgumentException($"{p}fallbackDistanceBlocks must be 1 to {ProductionTransportSettingsDto.MaxUnits}.");
            if (t.AwaitingSpaceMinutes is < 1 or > ProductionTransportSettingsDto.MaxMinutes)
                throw new System.ArgumentException($"{p}awaitingSpaceMinutes must be 1 to {ProductionTransportSettingsDto.MaxMinutes}.");
            if (t.MaxReroutes is < 0 or > 10)
                throw new System.ArgumentException($"{p}maxReroutes must be 0 to 10.");
            if (t.LaneMaxAgeHours is < 1 or > 8760)
                throw new System.ArgumentException($"{p}laneMaxAgeHours must be 1 to 8760.");
            return new ProductionTransportSettingsDto
            {
                WalkingSpeedBlocksPerSecond = t.WalkingSpeedBlocksPerSecond,
                DefaultCarrierCapacity = t.DefaultCarrierCapacity,
                DefaultThresholdPercent = t.DefaultThresholdPercent,
                DefaultThresholdUnits = t.DefaultThresholdUnits,
                UnmeasuredRouteFactor = t.UnmeasuredRouteFactor,
                FallbackDistanceBlocks = t.FallbackDistanceBlocks,
                AwaitingSpaceMinutes = t.AwaitingSpaceMinutes,
                MaxReroutes = t.MaxReroutes,
                LaneMaxAgeHours = t.LaneMaxAgeHours,
            };
        }

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
