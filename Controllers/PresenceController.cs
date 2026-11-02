using knkwebapi_v2.Attributes;
using knkwebapi_v2.Extensions;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Realtime.Presence;
using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// WT2 (PandiO/knk-workspace#181, DESIGN §8.3/§8.5): who is online in game, from the game hub's presence registry. Only
    /// players on a <b>connected</b> game server count (an offer can't reach a server in its grace period); with the realtime
    /// channel off nobody is online and the web app shows the task code.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class PresenceController : ControllerBase
    {
        /// <summary>Nodes the "Hand off to…" picker may filter on (it lists staff, not arbitrary permission holders).</summary>
        public static readonly string[] OnlineStaffNodes = { WorldTaskNodes.Tasks, StaffPermissions.ManageWorld };

        private readonly PresenceRegistry _presence;
        private readonly KnKDbContext _db;
        private readonly IPermissionResolutionService _permissions;

        public PresenceController(PresenceRegistry presence, KnKDbContext db, IPermissionResolutionService permissions)
        {
            _presence = presence;
            _db = db;
            _permissions = permissions;
        }

        /// <summary>Whether the logged-in caller is online in game now ("Send to Minecraft" offers it to them, else the task code).</summary>
        [HttpGet("me")]
        public async Task<ActionResult<PresenceMeDto>> Me()
        {
            if (User.GetUserId() is not { } userId) return Unauthorized(new { error = "Unauthorized", message = "Log in to use this." });
            var uuid = await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Uuid).FirstOrDefaultAsync();
            var serverId = _presence.ConnectedServerOf(uuid);
            return Ok(new PresenceMeDto { Online = serverId != null, ServerId = serverId, HasMinecraftAccount = !string.IsNullOrWhiteSpace(uuid) });
        }

        /// <summary>
        /// The "Hand off to…" picker: online players who hold <paramref name="node"/> (default <c>knk.tasks</c>; also
        /// <c>knk.admin.world</c>). Staff with <c>knk.admin.world</c> only (they create world tasks).
        /// </summary>
        [HttpGet("online-staff")]
        [RequirePermission(StaffPermissions.ManageWorld)]
        public async Task<ActionResult<List<OnlineStaffDto>>> OnlineStaff([FromQuery] string? node = null)
        {
            var wanted = string.IsNullOrWhiteSpace(node) ? WorldTaskNodes.Tasks : node.Trim();
            if (!OnlineStaffNodes.Contains(wanted, StringComparer.Ordinal))
            {
                return BadRequest(new { error = "UnknownNode", message = $"node must be one of: {string.Join(", ", OnlineStaffNodes)}." });
            }

            var online = _presence.ConnectedPlayers();
            if (online.Count == 0) return Ok(new List<OnlineStaffDto>());
            var byUuid = online.GroupBy(p => p.Player.Uuid, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var uuids = byUuid.Keys.ToList();
            var users = await _db.Users.AsNoTracking()
                .Where(u => u.Uuid != null && uuids.Contains(u.Uuid.ToLower()))
                .Select(u => new { u.Id, u.Username, u.Uuid })
                .ToListAsync();

            var result = new List<OnlineStaffDto>();
            foreach (var user in users.OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase))
            {
                var check = await _permissions.CheckAsync(user.Id, wanted);
                if (check?.Allowed != true) continue;
                var entry = byUuid[PresenceRegistry.NormalizeUuid(user.Uuid)];
                result.Add(new OnlineStaffDto
                {
                    UserId = user.Id,
                    Username = user.Username,
                    ServerId = entry.ServerId,
                    World = entry.Player.World,
                });
            }
            return Ok(result);
        }
    }

    public class PresenceMeDto
    {
        [JsonPropertyName("online")] public bool Online { get; set; }
        [JsonPropertyName("serverId")] public string? ServerId { get; set; }
        /// <summary>False when the account has no Minecraft UUID linked (it can never be online in game).</summary>
        [JsonPropertyName("hasMinecraftAccount")] public bool HasMinecraftAccount { get; set; }
    }

    public class OnlineStaffDto
    {
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
        [JsonPropertyName("serverId")] public string ServerId { get; set; } = string.Empty;
        [JsonPropertyName("world")] public string? World { get; set; }
    }
}
