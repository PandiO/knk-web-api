using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Production
{
    /// <summary>
    /// Who is looking at a structure or storage (P6a, PandiO/knk-workspace#683, decision record § 15.1 M3): the logged-in
    /// web user, or the player the game server names in <c>X-Acting-User-Id</c>. A plugin call without a player, and every
    /// anonymous caller, is a visitor: public fields only (A25).
    /// </summary>
    public sealed record ProductionViewer(int? UserId, bool IsStaff)
    {
        public static ProductionViewer Visitor { get; } = new(null, false);
    }

    /// <summary>
    /// Whether a user is a member of a town. The API has no town membership yet (town roles are #246); until then
    /// membership is the permission node <c>knk.town.member.&lt;townId&gt;</c> (wildcard-aware, so
    /// <c>knk.town.member.*</c> covers every town), granted through permission groups. #246 replaces this class.
    /// </summary>
    public interface ITownMembershipResolver
    {
        Task<bool> IsMemberAsync(int userId, int townId, CancellationToken ct = default);
    }

    public sealed class PermissionTownMembershipResolver : ITownMembershipResolver
    {
        public const string NodePrefix = "knk.town.member.";

        private readonly IPermissionResolutionService _permissions;

        public PermissionTownMembershipResolver(IPermissionResolutionService permissions) => _permissions = permissions;

        public static string NodeFor(int townId) => NodePrefix + townId;

        public async Task<bool> IsMemberAsync(int userId, int townId, CancellationToken ct = default) =>
            (await _permissions.CheckAsync(userId, NodeFor(townId)))?.Allowed == true;
    }

    public interface IProductionViewerResolver
    {
        Task<ProductionViewer> ResolveAsync(KnkCaller caller, CancellationToken ct = default);

        /// <summary>Staff (<c>knk.admin.world</c>) and members of <paramref name="townId"/> see the details (M3).</summary>
        Task<bool> SeesDetailsAsync(ProductionViewer viewer, int? townId, CancellationToken ct = default);

        /// <summary>Whether the viewer may see (and use, through a terminal) a storage: staff; members of the town of
        /// the domain that holds it; the player who holds a player storage.</summary>
        Task<bool> SeesStorageAsync(ProductionViewer viewer, Storage storage, CancellationToken ct = default);
    }

    public sealed class ProductionViewerResolver : IProductionViewerResolver
    {
        private readonly KnKDbContext _db;
        private readonly IPermissionResolutionService _permissions;
        private readonly ITownMembershipResolver _towns;

        public ProductionViewerResolver(KnKDbContext db, IPermissionResolutionService permissions, ITownMembershipResolver towns)
        {
            _db = db;
            _permissions = permissions;
            _towns = towns;
        }

        public async Task<ProductionViewer> ResolveAsync(KnkCaller caller, CancellationToken ct = default)
        {
            var userId = caller.IsWebUser ? caller.WebUserId : caller.IsPluginService ? caller.ActingUserId : null;
            if (userId is not > 0) return ProductionViewer.Visitor;
            var staff = (await _permissions.CheckAsync(userId.Value, StaffPermissions.ManageWorld))?.Allowed == true;
            return new ProductionViewer(userId, staff);
        }

        public async Task<bool> SeesDetailsAsync(ProductionViewer viewer, int? townId, CancellationToken ct = default)
        {
            if (viewer.IsStaff) return true;
            if (viewer.UserId == null || townId == null) return false;
            return await _towns.IsMemberAsync(viewer.UserId.Value, townId.Value, ct);
        }

        public async Task<bool> SeesStorageAsync(ProductionViewer viewer, Storage storage, CancellationToken ct = default)
        {
            if (viewer.IsStaff) return true;
            if (viewer.UserId == null) return false;
            if (storage.UserId.HasValue) return storage.UserId == viewer.UserId;
            if (storage.DomainId is not { } domainId) return false; // clan, transit and system storages: staff only
            return await SeesDetailsAsync(viewer, await TownOfDomainAsync(_db, domainId, ct), ct);
        }

        /// <summary>The town a domain belongs to: a town itself, a district's town, or a structure's effective town
        /// (its district's town, else its rural town). Null for other domains.</summary>
        public static async Task<int?> TownOfDomainAsync(KnKDbContext db, int domainId, CancellationToken ct = default)
        {
            if (await db.Set<Town>().AnyAsync(t => t.Id == domainId, ct)) return domainId;
            var district = await db.Set<District>().Where(d => d.Id == domainId).Select(d => (int?)d.TownId).FirstOrDefaultAsync(ct);
            if (district != null) return district;
            return await db.Structures.Where(s => s.Id == domainId)
                .Select(s => s.District != null ? (int?)s.District.TownId : s.TownId).FirstOrDefaultAsync(ct);
        }
    }
}
