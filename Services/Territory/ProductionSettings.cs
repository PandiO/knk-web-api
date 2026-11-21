using System.Linq;
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
            dto.Delivery ??= new ProductionDeliverySettingsDto();
            dto.Harvest ??= new ProductionHarvestSettingsDto();
            dto.Logistics ??= new ProductionLogisticsSettingsDto();
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
            Delivery = ValidateDelivery(dto.Delivery ?? stored?.Delivery),
            Harvest = ValidateHarvest(dto.Harvest ?? stored?.Harvest),
            Logistics = ValidateLogistics(dto.Logistics ?? stored?.Logistics),
        };

        private static ProductionLogisticsSettingsDto ValidateLogistics(ProductionLogisticsSettingsDto? l)
        {
            l ??= new ProductionLogisticsSettingsDto();
            const string p = "production.logistics.";
            if (l.PlannerIntervalMinutes is < 1 or > 1440)
                throw new System.ArgumentException($"{p}plannerIntervalMinutes must be 1 to 1440.");
            if (l.ReservationMinutes is < 1 or > 10_080)
                throw new System.ArgumentException($"{p}reservationMinutes must be 1 to 10080.");
            if (l.ConsumptionWindowDays is < 1 or > 90)
                throw new System.ArgumentException($"{p}consumptionWindowDays must be 1 to 90.");
            if (l.DefaultReorderHours is < 0 or > 720 || l.DefaultTargetHours is <= 0 or > 720 || l.DefaultTargetHours < l.DefaultReorderHours)
                throw new System.ArgumentException($"{p}defaultReorderHours (0 to 720) must be at most defaultTargetHours (above 0, at most 720).");
            if (l.RunRetentionDays is < 1 or > 365)
                throw new System.ArgumentException($"{p}runRetentionDays must be 1 to 365.");
            return new ProductionLogisticsSettingsDto
            {
                PlannerIntervalMinutes = l.PlannerIntervalMinutes,
                ReservationMinutes = l.ReservationMinutes,
                ConsumptionWindowDays = l.ConsumptionWindowDays,
                DefaultReorderHours = l.DefaultReorderHours,
                DefaultTargetHours = l.DefaultTargetHours,
                RunRetentionDays = l.RunRetentionDays,
            };
        }

        private static ProductionHarvestSettingsDto ValidateHarvest(ProductionHarvestSettingsDto? h)
        {
            h ??= new ProductionHarvestSettingsDto();
            const string p = "production.harvest.";
            if (h.DefaultPoolCapacity is <= 0 or > ProductionHarvestSettingsDto.MaxPoolCapacity)
                throw new System.ArgumentException($"{p}defaultPoolCapacity must be above 0 and at most {ProductionHarvestSettingsDto.MaxPoolCapacity}.");
            if (h.DefaultRegenHoursToFull is <= 0 or > 24 * 365)
                throw new System.ArgumentException($"{p}defaultRegenHoursToFull must be above 0 and at most {24 * 365}.");
            if (h.MaxEntriesPerReport is < 1 or > ProductionHarvestSettingsDto.MaxReportEntries)
                throw new System.ArgumentException($"{p}maxEntriesPerReport must be 1 to {ProductionHarvestSettingsDto.MaxReportEntries}.");
            return new ProductionHarvestSettingsDto
            {
                DefaultPoolCapacity = h.DefaultPoolCapacity,
                DefaultRegenHoursToFull = h.DefaultRegenHoursToFull,
                MaxEntriesPerReport = h.MaxEntriesPerReport,
            };
        }

        private static ProductionDeliverySettingsDto ValidateDelivery(ProductionDeliverySettingsDto? d)
        {
            d ??= new ProductionDeliverySettingsDto();
            const string p = "production.delivery.";
            const long max = ProductionDeliverySettingsDto.MaxCap;
            if (d.InboxBaseCapUnits is < 0 or > max) throw new System.ArgumentException($"{p}inboxBaseCapUnits must be 0 to {max}.");
            if (d.StashBaseCapUnits is < 0 or > max) throw new System.ArgumentException($"{p}stashBaseCapUnits must be 0 to {max}.");
            if (d.ExpiryDays is < 1 or > 365) throw new System.ArgumentException($"{p}expiryDays must be 1 to 365.");
            var reminders = (d.ReminderDaysBefore ?? new()).Distinct().OrderByDescending(x => x).ToList();
            if (reminders.Count > 5 || reminders.Any(x => x < 1 || x >= d.ExpiryDays))
                throw new System.ArgumentException($"{p}reminderDaysBefore: at most 5 values, each 1 to expiryDays - 1.");
            var warn = (d.WarnAtPercent ?? new()).Distinct().OrderBy(x => x).ToList();
            if (warn.Count > 5 || warn.Any(x => x < 1 || x > 100))
                throw new System.ArgumentException($"{p}warnAtPercent: at most 5 values, each 1 to 100.");
            var tiers = d.PremiumTiers ?? new();
            if (tiers.GroupBy(t => t.PermissionGroupId).Any(g => g.Count() > 1))
                throw new System.ArgumentException($"{p}premiumTiers: one entry per permission group.");
            if (tiers.Any(t => t.PermissionGroupId <= 0 || t.InboxCapUnits is < 0 or > max || t.StashCapUnits is < 0 or > max))
                throw new System.ArgumentException($"{p}premiumTiers: a permission group id and caps of 0 to {max}.");
            return new ProductionDeliverySettingsDto
            {
                InboxBaseCapUnits = d.InboxBaseCapUnits,
                StashBaseCapUnits = d.StashBaseCapUnits,
                ExpiryDays = d.ExpiryDays,
                ReminderDaysBefore = reminders,
                WarnAtPercent = warn,
                PremiumTiers = tiers.Select(t => new PremiumStorageCapDto
                {
                    PermissionGroupId = t.PermissionGroupId, InboxCapUnits = t.InboxCapUnits, StashCapUnits = t.StashCapUnits,
                }).ToList(),
            };
        }

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
