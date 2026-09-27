using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// One box in the world (DESIGN.md §3.2). Its type and box grade are rolled at spawn time; the item is rolled at
/// claim time. <see cref="Status"/> is a concurrency token, so two claims racing for the same box make the loser's
/// save fail. Written from Phase 2 on; not form-configurable.
/// </summary>
public class LootboxSpawn
{
    public int Id { get; set; }

    // Server-side identity carried in the entities' PDC; never matched by display name.
    public Guid Token { get; set; } = Guid.NewGuid();

    public int LootboxTypeId { get; set; }
    public LootboxType LootboxType { get; set; } = null!;

    public int BoxGradeId { get; set; }
    public Grade BoxGrade { get; set; } = null!;

    // SetNull: deleting an area keeps its spawn history.
    public int? SpawnAreaId { get; set; }
    public LootboxSpawnArea? SpawnArea { get; set; }

    public string World { get; set; } = null!;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    [ConcurrencyCheck]
    public LootboxSpawnStatus Status { get; set; } = LootboxSpawnStatus.Active;

    public DateTime SpawnedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? ClaimedAt { get; set; }

    public int? ClaimedByUserId { get; set; }
    public User? ClaimedByUser { get; set; }

    public string? ServerId { get; set; }

    // Set for an admin spawn (/knk lootbox spawn).
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
}
