using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Regions;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.ValidationMethods
{
    /// <summary>
    /// <c>SiegeWeaponSpotSize</c> (PandiO/knk-workspace#988, SIEGE_WEAPONS W70, A02): on the spot form's region field
    /// (<c>WgRegionId</c>), depending on <c>Facing</c>. Asks the game server for the region's bounds over the game hub and
    /// shows the size class that fits for the chosen facing (width across x length along the facing x height; a polygon
    /// counts as its bounding box), or "too small" when even the small class doesn't fit. Seeded blocking, as the save
    /// refuses a region that is too small anyway (<see cref="SiegeWeaponService"/>). A game server that can't be asked
    /// gives a passing result with a <c>failureReason</c>, so the form says it couldn't check and the save goes on.
    /// </summary>
    public class SiegeWeaponSpotSizeValidator : IValidationMethod
    {
        public const string Type = "SiegeWeaponSpotSize";

        private readonly KnKDbContext _db;
        private readonly IGameRegionQueryClient _regions;

        public SiegeWeaponSpotSizeValidator(KnKDbContext db, IGameRegionQueryClient regions)
        {
            _db = db;
            _regions = regions;
        }

        public string ValidationType => Type;

        /// <summary>The region can be redefined in game, so a stored verdict expires (KNG-269).</summary>
        public bool ReadsExternalState => true;

        public async Task<ValidationMethodResult> ValidateAsync(object? fieldValue, object? dependencyValue, string? configJson,
            Dictionary<string, object>? formContextData)
        {
            var region = SiegeWeaponSpotRuleInput.Text(fieldValue);
            if (region == null) return SiegeWeaponSpotRuleInput.Passed("Draw a box-shaped region or pick one; its size class is shown here.");
            var ctx = formContextData ?? new Dictionary<string, object>();
            var facing = SiegeWeaponSpotRuleInput.Facing(dependencyValue) ?? SiegeWeaponSpotRuleInput.Facing(SiegeWeaponSpotRuleInput.Get(ctx, "Facing"))
                ?? SiegeWeaponFacing.North;
            var world = await SiegeWeaponSpotRuleInput.WorldAsync(_db, ctx);

            var answer = await _regions.GetGeometryAsync(region, world);
            if (answer.Status == RegionQueryStatus.Unavailable)
                return SiegeWeaponSpotRuleInput.NotChecked("GameServerUnavailable", $"Size class not checked: {answer.Message} It is worked out again when the weapon is placed.");
            if (answer.Status == RegionQueryStatus.NotFound || answer.Geometry == null)
                return SiegeWeaponSpotRuleInput.Failed(SiegeWeaponSpotRuleInput.SizeReason, answer.Message);

            var g = answer.Geometry;
            var (width, length, height) = SiegeWeaponSpotSizing.Footprint(g.MinX, g.MinY, g.MinZ, g.MaxX, g.MaxY, g.MaxZ, facing);
            var box = g.IsCuboid ? "" : $" The region is a {g.Shape}; its bounding box counts, so draw a box to be sure the weapon fits.";
            var sizeClass = SiegeWeaponSpotSizing.ClassFor(g.MinX, g.MinY, g.MinZ, g.MaxX, g.MaxY, g.MaxZ, facing);
            if (sizeClass == null)
            {
                return SiegeWeaponSpotRuleInput.Failed(SiegeWeaponSpotRuleInput.SizeReason, 
                    $"The region is too small for a weapon facing {facing}: {width} wide x {length} long x {height} high, the small class needs at least {SiegeWeaponSpotSizing.Describe(SiegeWeaponSizeClass.Small)}.{box}");
            }
            var result = SiegeWeaponSpotRuleInput.Passed(
                $"Size class {sizeClass}: the region is {width} wide x {length} long x {height} high facing {facing}.{box}");
            result.Metadata = new Dictionary<string, object> { { "sizeClass", sizeClass.Value.ToString() } };
            return result;
        }
    }

    /// <summary>
    /// <c>SiegeWeaponSpotSite</c> (PandiO/knk-workspace#988, SIEGE_WEAPONS § 4.6, W44): on the spot form's region field.
    /// A weapon is pasted into the spot exactly, so this warns when the layer under the region isn't solid or its space
    /// isn't clear (knk-core <c>SpotSiteCheck</c>, answered by the game server over the game hub). Seeded as a warning. Only
    /// a game server with <c>siege.weapons.enabled</c> on answers; otherwise the rule says it couldn't check.
    /// </summary>
    public class SiegeWeaponSpotSiteValidator : IValidationMethod
    {
        public const string Type = "SiegeWeaponSpotSite";

        private readonly KnKDbContext _db;
        private readonly IGameRegionQueryClient _regions;

        public SiegeWeaponSpotSiteValidator(KnKDbContext db, IGameRegionQueryClient regions)
        {
            _db = db;
            _regions = regions;
        }

        public string ValidationType => Type;

        /// <summary>Blocks in the world change outside the form, so a stored verdict expires (KNG-269).</summary>
        public bool ReadsExternalState => true;

        public async Task<ValidationMethodResult> ValidateAsync(object? fieldValue, object? dependencyValue, string? configJson,
            Dictionary<string, object>? formContextData)
        {
            var region = SiegeWeaponSpotRuleInput.Text(fieldValue);
            if (region == null) return SiegeWeaponSpotRuleInput.Passed("");
            var world = await SiegeWeaponSpotRuleInput.WorldAsync(_db, formContextData ?? new Dictionary<string, object>());

            var answer = await _regions.CheckSpotSiteAsync(region, world);
            if (answer.Status == RegionQueryStatus.Unavailable)
                return SiegeWeaponSpotRuleInput.NotChecked("GameServerUnavailable",
                    $"Floor and space not checked: {answer.Message} The game server answers only with siege.weapons.enabled on.");
            if (answer.Status == RegionQueryStatus.NotFound)
                return SiegeWeaponSpotRuleInput.Failed(SiegeWeaponSpotRuleInput.SiteReason, answer.Message);
            if (!answer.HasWarnings)
                return SiegeWeaponSpotRuleInput.Passed("The floor is solid and the space is clear.");
            var lines = answer.Warnings.Count > 0 ? answer.Warnings : new List<string> { answer.Message };
            return SiegeWeaponSpotRuleInput.Failed(SiegeWeaponSpotRuleInput.SiteReason, string.Join(" ", lines));
        }
    }

    /// <summary>The form values the two spot rules read, and their result shapes.</summary>
    internal static class SiegeWeaponSpotRuleInput
    {
        /// <summary>
        /// The placeholders the seeded error messages show: <c>{sizeReason}</c> and <c>{siteReason}</c>. Two names, because
        /// the API merges the placeholders of every rule on a field (both rules sit on the region field).
        /// </summary>
        public const string SizeReason = "sizeReason";
        public const string SiteReason = "siteReason";

        /// <summary>The world of the spot's scenario: its town's world, else the hub location's; null asks every loaded world.</summary>
        public static async Task<string?> WorldAsync(KnKDbContext db, Dictionary<string, object> ctx)
        {
            return Id(Get(ctx, "SiegeScenarioId")) is int id and > 0 ? await SiegeScenarioWorld.OfAsync(db, id) : null;
        }

        public static ValidationMethodResult Passed(string message) =>
            new() { IsValid = true, Message = message, Placeholders = new Dictionary<string, string>() };

        public static ValidationMethodResult Failed(string placeholder, string reason) => new()
        {
            IsValid = false,
            Message = reason,
            Placeholders = new Dictionary<string, string> { { placeholder, reason } },
        };

        /// <summary>The check couldn't run: passes (never blocks a save), with the reason the form shows.</summary>
        public static ValidationMethodResult NotChecked(string reason, string message) => new()
        {
            IsValid = true,
            Message = message,
            Metadata = new Dictionary<string, object> { { "failureReason", reason } },
            Placeholders = new Dictionary<string, string>(),
        };

        public static object? Get(Dictionary<string, object> ctx, string name) =>
            ctx.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

        public static string? Text(object? v) => v switch
        {
            null => null,
            string s => string.IsNullOrWhiteSpace(s) ? null : s.Trim(),
            JsonElement { ValueKind: JsonValueKind.String } e => string.IsNullOrWhiteSpace(e.GetString()) ? null : e.GetString()!.Trim(),
            JsonElement => null,
            var o => string.IsNullOrWhiteSpace(o.ToString()) ? null : o.ToString()!.Trim(),
        };

        public static SiegeWeaponFacing? Facing(object? v)
        {
            if (v is SiegeWeaponFacing f) return f;
            if (v is JsonElement { ValueKind: JsonValueKind.Number } n && n.TryGetInt32(out var i) && Enum.IsDefined(typeof(SiegeWeaponFacing), i))
                return (SiegeWeaponFacing)i;
            if (v is int j && Enum.IsDefined(typeof(SiegeWeaponFacing), j)) return (SiegeWeaponFacing)j;
            var text = Text(v);
            return text != null && Enum.TryParse<SiegeWeaponFacing>(text, true, out var parsed) && Enum.IsDefined(parsed) ? parsed : null;
        }

        public static int? Id(object? v) => v switch
        {
            null => null,
            int i => i,
            long l => (int)l,
            string t => int.TryParse(t, out var p) ? p : null,
            JsonElement { ValueKind: JsonValueKind.Number } e => e.TryGetInt32(out var n) ? n : null,
            JsonElement { ValueKind: JsonValueKind.String } e => int.TryParse(e.GetString(), out var p) ? p : null,
            JsonElement { ValueKind: JsonValueKind.Object } e => e.EnumerateObject()
                .Where(p => string.Equals(p.Name, "id", StringComparison.OrdinalIgnoreCase)).Select(p => Id(p.Value)).FirstOrDefault(),
            IDictionary<string, object> d => d.Where(kv => string.Equals(kv.Key, "id", StringComparison.OrdinalIgnoreCase))
                .Select(kv => Id(kv.Value)).FirstOrDefault(),
            _ => null,
        };
    }

    /// <summary>The world a siege scenario is in (its town's, else its hub location's), or null.</summary>
    public static class SiegeScenarioWorld
    {
        public static async Task<string?> OfAsync(KnKDbContext db, int siegeScenarioId)
        {
            if (siegeScenarioId <= 0) return null;
            var row = await db.SiegeScenarios.AsNoTracking().Where(s => s.Id == siegeScenarioId)
                .Select(s => new
                {
                    Town = s.Town != null ? s.Town.WorldName : null,
                    Hub = s.HubLocation != null ? s.HubLocation.World : null,
                }).FirstOrDefaultAsync();
            var world = row?.Town ?? row?.Hub;
            return string.IsNullOrWhiteSpace(world) ? null : world.Trim();
        }
    }
}
