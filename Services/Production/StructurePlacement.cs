using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Services.Production
{
    /// <summary>The placement fields a structure form sends (0 or null = not set).</summary>
    public readonly record struct StructurePlacementInput(int? DistrictId, int? TownId, int? StreetId, int HouseNumber);

    /// <summary>The checked placement: exactly one of <see cref="DistrictId"/> and <see cref="TownId"/> is set.</summary>
    public readonly record struct StructurePlacementResult(int? DistrictId, int? TownId, int? StreetId, int HouseNumber,
        District? District, Town? Town);

    /// <summary>
    /// Where a structure stands (production DESIGN § 3.3-3.4, R1; P2, PandiO/knk-workspace#91): in a district
    /// (<c>DistrictId</c>, with a street and house number as before) or rural (<c>TownId</c>, no district, no street,
    /// house number 0). Exactly one of the two is set, here and in check constraint <c>CK_structures_DistrictXorTown</c>.
    /// Also runs the placement rules (P-1 to P-3) of the type's default form when a structure is saved through the API
    /// (DESIGN § 3.4: "a rule set to block refuses the save there too").
    /// </summary>
    public interface IStructurePlacement
    {
        /// <summary>Checks the District-xor-Town rule, the street and house number, and that the district, town and
        /// street exist. Throws <see cref="ArgumentException"/> (400) with the reason.</summary>
        Task<StructurePlacementResult> ValidateAsync(StructurePlacementInput input, string paramName);

        /// <summary>Runs the field rules of <paramref name="entityTypeName"/>'s default form against
        /// <paramref name="structure"/> (its District and Town navigations loaded). A failed blocking rule throws
        /// <see cref="ArgumentException"/>; failed warnings are returned (and rules that could not run, e.g. the game
        /// server is offline, as warnings too).</summary>
        Task<List<string>> CheckRulesAsync(string entityTypeName, Structure structure);
    }

    public sealed class StructurePlacement : IStructurePlacement
    {
        private readonly KnKDbContext _db;
        private readonly ISavedEntityRuleValidator? _rules;
        private readonly ILogger<StructurePlacement>? _logger;

        public StructurePlacement(KnKDbContext db, ISavedEntityRuleValidator? rules = null, ILogger<StructurePlacement>? logger = null)
        {
            _db = db;
            _rules = rules;
            _logger = logger;
        }

        /// <summary>The structure's town: its district's town, or its own <c>TownId</c> when rural.</summary>
        public static int? EffectiveTownId(int? districtTownId, int? townId) => districtTownId ?? townId;

        /// <summary>The District-xor-Town rule, the street and the house number, without database reads.</summary>
        public static StructurePlacementInput Normalize(StructurePlacementInput input, string paramName)
        {
            int? districtId = input.DistrictId is > 0 ? input.DistrictId : null;
            int? townId = input.TownId is > 0 ? input.TownId : null;
            int? streetId = input.StreetId is > 0 ? input.StreetId : null;

            if (districtId.HasValue == townId.HasValue)
                throw new ArgumentException(
                    "A structure is either in a district (districtId) or rural (townId): set exactly one of the two.", paramName);
            if (districtId.HasValue)
            {
                if (!streetId.HasValue) throw new ArgumentException("StreetId is required for a structure in a district.", paramName);
                if (input.HouseNumber <= 0) throw new ArgumentException("HouseNumber must be greater than 0.", paramName);
                return new StructurePlacementInput(districtId, null, streetId, input.HouseNumber);
            }
            if (streetId.HasValue)
                throw new ArgumentException("A rural structure (townId) has no street: leave streetId empty.", paramName);
            return new StructurePlacementInput(null, townId, null, 0);
        }

        public async Task<StructurePlacementResult> ValidateAsync(StructurePlacementInput input, string paramName)
        {
            var n = Normalize(input, paramName);
            int? districtId = n.DistrictId, townId = n.TownId, streetId = n.StreetId;

            if (districtId.HasValue)
            {
                var district = await _db.Districts.Include(d => d.Town).FirstOrDefaultAsync(d => d.Id == districtId.Value)
                    ?? throw new ArgumentException($"District with id {districtId} not found.", paramName);
                if (!await _db.Streets.AnyAsync(s => s.Id == streetId!.Value))
                    throw new ArgumentException($"Street with id {streetId} not found.", paramName);
                return new StructurePlacementResult(districtId, null, streetId, n.HouseNumber, district, null);
            }

            var town = await _db.Towns.FirstOrDefaultAsync(t => t.Id == townId!.Value)
                ?? throw new ArgumentException($"Town with id {townId} not found.", paramName);
            return new StructurePlacementResult(null, townId, null, 0, null, town);
        }

        public async Task<List<string>> CheckRulesAsync(string entityTypeName, Structure structure)
        {
            var warnings = new List<string>();
            if (_rules == null) return warnings;
            var validation = await _rules.ValidateAsync(entityTypeName, structure);
            foreach (var check in validation.Results.Where(r => !r.Result.IsValid))
            {
                if (check.CouldNotRun)
                {
                    warnings.Add($"{check.FieldLabel}: not checked ({check.Message})");
                    continue;
                }
                if (check.Result.IsBlocking)
                    throw new ArgumentException($"{check.FieldLabel}: {check.Message}");
                warnings.Add($"{check.FieldLabel}: {check.Message}");
            }
            if (warnings.Count > 0)
                _logger?.LogInformation("{Type} {Name}: saved with placement warnings: {Warnings}",
                    entityTypeName, structure.Name, string.Join(" | ", warnings));
            return warnings;
        }
    }
}
