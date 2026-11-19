using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Production
{
    /// <summary>
    /// Writes the Structure part of a structure subtype (P2, PandiO/knk-workspace#91), the way
    /// <see cref="StructureService"/> writes a plain structure: the name, the region, the placement (District-xor-Town,
    /// <see cref="IStructurePlacement"/>), the warp settings, the world (KNG-111), the spawn location (KNG-195) and the
    /// navigation defaults. The territory fields (#326) are never written here.
    /// </summary>
    public sealed class StructureBaseWriter
    {
        private readonly KnKDbContext _db;
        private readonly IStructurePlacement _placement;
        private readonly ILocationService _locations;
        private readonly ITeleportDestinationService _teleports;
        private readonly IDomainRegionNameFinalizer _regionNames;
        private readonly IDomainWorldResolver _worlds;

        public StructureBaseWriter(KnKDbContext db, IStructurePlacement placement, ILocationService locations,
            ITeleportDestinationService teleports, IDomainRegionNameFinalizer regionNames, IDomainWorldResolver worlds)
        {
            _db = db;
            _placement = placement;
            _locations = locations;
            _teleports = teleports;
            _regionNames = regionNames;
            _worlds = worlds;
        }

        public IStructurePlacement Placement => _placement;

        /// <summary>Checks and applies the Structure fields of <paramref name="dto"/> to <paramref name="target"/>
        /// (new when <paramref name="isCreate"/>). Throws <see cref="ArgumentException"/> (400).</summary>
        public async Task ApplyAsync(Structure target, StructureDto dto, string typeName, bool isCreate)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            DomainNameRules.Normalize(dto, typeName, nameof(dto));
            if (string.IsNullOrWhiteSpace(dto.WgRegionId)) throw new ArgumentException("WgRegionId is required.", nameof(dto));
            var placement = await _placement.ValidateAsync(
                new StructurePlacementInput(dto.DistrictId, dto.TownId, dto.StreetId, dto.HouseNumber), nameof(dto));
            await _teleports.ValidateSettingsAsync(dto);

            // KNG-111: before any Location is saved, so a rejected world leaves nothing behind.
            string worldName = await _worlds.ResolveAsync(new DomainWorldRequest
            {
                DomainId = isCreate ? null : target.Id,
                RequestedWorld = dto.WorldName,
                WgRegionId = dto.WgRegionId,
                LocationWorld = dto.Location?.World,
                LocationId = dto.Location?.Id ?? dto.LocationId,
                ParentDomainId = placement.DistrictId ?? placement.TownId,
            });
            int? locationId = await ResolveLocationAsync(dto);

            target.Name = dto.Name;
            target.Description = dto.Description ?? string.Empty;
            target.AllowEntry = dto.AllowEntry ?? true;
            target.AllowExit = dto.AllowExit ?? true;
            target.WgRegionId = dto.WgRegionId;
            target.WorldName = worldName;
            target.LocationId = locationId;
            target.StreetId = placement.StreetId;
            target.HouseNumber = placement.HouseNumber;
            target.DistrictId = placement.DistrictId;
            target.District = placement.District;
            target.TownId = placement.TownId;
            target.Town = placement.Town;
            if (isCreate) target.CreatedAt = DateTime.UtcNow;
            DomainTeleportSettings.Apply(target, dto);
            DomainNavigationDefaults.Apply(target, dto);
        }

        /// <summary>Gives a region drawn through a world task its final name once the structure is saved (KNG-103).</summary>
        public Task<bool> FinalizeRegionAsync(Structure saved, string? previousRegionId = null) =>
            _regionNames.FinalizeAsync(saved, previousRegionId);

        private async Task<int?> ResolveLocationAsync(StructureDto dto)
        {
            if (dto.Location == null)
            {
                if (!dto.LocationId.HasValue) return null;
                if (!await _db.Locations.AnyAsync(l => l.Id == dto.LocationId.Value))
                    throw new ArgumentException($"Location with id {dto.LocationId} not found.", nameof(dto));
                return dto.LocationId;
            }
            if (!dto.Location.Id.HasValue || dto.Location.Id.Value == 0)
            {
                var created = await _locations.CreateAsync(dto.Location);
                return created.Id;
            }
            await _locations.UpdateAsync(dto.Location.Id.Value, dto.Location);
            return dto.Location.Id.Value;
        }
    }
}
