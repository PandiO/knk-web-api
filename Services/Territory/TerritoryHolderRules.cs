using System;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services
{
    /// <summary>The town's territory fields on save (territory DESIGN § 1-2, PandiO/knk-workspace#326).</summary>
    public interface ITerritoryHolderRules
    {
        /// <summary>Throws <see cref="ArgumentException"/> when power is negative, the kingdom or province is unknown, or
        /// the province is not in the town's kingdom. Does nothing when <c>power</c> is null (not on this form).</summary>
        Task ValidateTownAsync(TownDto dto);
    }

    public sealed class TerritoryHolderRules : ITerritoryHolderRules
    {
        public const int MaxPower = 1_000_000;

        private readonly KnKDbContext _db;

        public TerritoryHolderRules(KnKDbContext db) => _db = db;

        public async Task ValidateTownAsync(TownDto dto)
        {
            if (dto?.Power == null) return;
            if (dto.Power < 0 || dto.Power > MaxPower)
                throw new ArgumentException($"Power must be between 0 and {MaxPower}.", nameof(dto));
            if (dto.KingdomId.HasValue && !await _db.Kingdoms.AnyAsync(k => k.Id == dto.KingdomId.Value))
                throw new ArgumentException($"Kingdom with id {dto.KingdomId} not found.", nameof(dto));
            if (!dto.ProvinceId.HasValue) return;
            var province = await _db.Provinces.AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.ProvinceId.Value)
                ?? throw new ArgumentException($"Province with id {dto.ProvinceId} not found.", nameof(dto));
            if (province.KingdomId != dto.KingdomId)
                throw new ArgumentException(
                    $"Province '{province.Name}' belongs to another kingdom: a town's province must be in the town's kingdom.",
                    nameof(dto));
        }

        /// <summary>Applies power, kingdom and province when <c>power</c> is on the form (non-null).</summary>
        public static void ApplyTown(Town town, TownDto dto)
        {
            if (dto?.Power == null) return;
            town.Power = dto.Power.Value;
            town.KingdomId = dto.KingdomId;
            town.ProvinceId = dto.ProvinceId;
        }
    }
}
