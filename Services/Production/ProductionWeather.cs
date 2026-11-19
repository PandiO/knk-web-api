using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Production
{
    /// <summary>
    /// Custom weather (PRODUCTION_KINDS § 4.4, P3 minimal): climate zones with odds, rolled once per in-game day per zone
    /// and stored. The roll is derived from the zone and the day (SHA-256), so replaying a day gives the same weather;
    /// the stored row wins if the odds changed since.
    /// </summary>
    public interface IProductionWeatherService
    {
        /// <summary>The in-game day containing <paramref name="at"/> for a day length of <paramref name="dayMinutes"/>.</summary>
        long DayOf(DateTime at, int dayMinutes);

        /// <summary>The zone of a town: its assigned zone, else the default zone (null when there is none).</summary>
        Task<ClimateZone?> ZoneForTownAsync(int? townId, CancellationToken ct = default);

        /// <summary>The stored roll of the zone's day, rolling and storing it when it is missing.</summary>
        Task<WeatherRoll> GetOrRollAsync(int climateZoneId, long day, int dayMinutes, CancellationToken ct = default);

        /// <summary>Rolls today's weather for every zone that has none yet; returns how many were rolled.</summary>
        Task<int> RollAllAsync(DateTime now, int dayMinutes, CancellationToken ct = default);
    }

    public sealed class ProductionWeatherService : IProductionWeatherService
    {
        private readonly KnKDbContext _db;
        private readonly TimeProvider _clock;

        public ProductionWeatherService(KnKDbContext db, TimeProvider? clock = null)
        {
            _db = db;
            _clock = clock ?? TimeProvider.System;
        }

        public long DayOf(DateTime at, int dayMinutes)
        {
            var minutes = (long)Math.Floor((DateTime.SpecifyKind(at, DateTimeKind.Utc) - DateTime.UnixEpoch).TotalMinutes);
            return Math.DivRem(minutes, dayMinutes, out var rem) - (rem < 0 ? 1 : 0);
        }

        public async Task<ClimateZone?> ZoneForTownAsync(int? townId, CancellationToken ct = default)
        {
            if (townId.HasValue)
            {
                var assigned = await _db.ClimateZoneTowns.AsNoTracking().Where(t => t.TownId == townId.Value)
                    .Select(t => t.ClimateZone).FirstOrDefaultAsync(ct);
                if (assigned != null) return assigned;
            }
            return await _db.ClimateZones.AsNoTracking().Where(z => z.IsDefault).OrderBy(z => z.Id).FirstOrDefaultAsync(ct);
        }

        /// <summary>The deterministic roll in [0, 100) for a zone and day.</summary>
        public static decimal RollValueOf(int climateZoneId, long day)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"knk-weather:{climateZoneId}:{day}"));
            ulong n = BitConverter.ToUInt64(hash, 0);
            return (n % 1_000_000UL) / 10_000m;
        }

        /// <summary>Picks the weather type for a roll: odds in ascending weather type id, cumulated; above the sum is clear.</summary>
        public static int? Pick(IEnumerable<ClimateZoneWeather> odds, decimal roll)
        {
            decimal cumulative = 0m;
            foreach (var o in odds.OrderBy(o => o.WeatherTypeId))
            {
                if (o.ChancePercent <= 0) continue;
                cumulative += o.ChancePercent;
                if (roll < cumulative) return o.WeatherTypeId;
            }
            return null;
        }

        public async Task<WeatherRoll> GetOrRollAsync(int climateZoneId, long day, int dayMinutes, CancellationToken ct = default)
        {
            var existing = await _db.WeatherRolls.Include(r => r.WeatherType)
                .FirstOrDefaultAsync(r => r.ClimateZoneId == climateZoneId && r.Day == day, ct);
            if (existing != null) return existing;

            var odds = await _db.ClimateZoneWeathers.AsNoTracking().Where(o => o.ClimateZoneId == climateZoneId).ToListAsync(ct);
            var value = RollValueOf(climateZoneId, day);
            var start = DateTime.UnixEpoch.AddMinutes((double)(day * dayMinutes));
            var roll = new WeatherRoll
            {
                ClimateZoneId = climateZoneId,
                Day = day,
                DayStartsAt = start,
                DayEndsAt = start.AddMinutes(dayMinutes),
                RollValue = value,
                WeatherTypeId = Pick(odds, value),
                RolledAt = _clock.GetUtcNow().UtcDateTime,
            };
            _db.WeatherRolls.Add(roll);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Another sweep stored the same day first (unique zone + day): use its row.
                _db.Entry(roll).State = EntityState.Detached;
                return await _db.WeatherRolls.Include(r => r.WeatherType)
                    .FirstAsync(r => r.ClimateZoneId == climateZoneId && r.Day == day, ct);
            }
            if (roll.WeatherTypeId.HasValue)
                await _db.Entry(roll).Reference(r => r.WeatherType).LoadAsync(ct);
            return roll;
        }

        public async Task<int> RollAllAsync(DateTime now, int dayMinutes, CancellationToken ct = default)
        {
            long day = DayOf(now, dayMinutes);
            var zoneIds = await _db.ClimateZones.AsNoTracking().Select(z => z.Id).ToListAsync(ct);
            var rolled = await _db.WeatherRolls.AsNoTracking().Where(r => r.Day == day).Select(r => r.ClimateZoneId).ToListAsync(ct);
            int count = 0;
            foreach (var id in zoneIds.Except(rolled))
            {
                await GetOrRollAsync(id, day, dayMinutes, ct);
                count++;
            }
            return count;
        }
    }
}
