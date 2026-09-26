namespace knkwebapi_v2.Models;

/// <summary>
/// Singleton lootbox settings (DESIGN.md §3.2), same fixed "global" Id pattern as <see cref="SalaryConfiguration"/>.
/// </summary>
public class LootboxConfiguration
{
    public string Id { get; set; } = "global";

    // Global switch; the types themselves are seeded disabled (D9).
    public bool Enabled { get; set; } = true;

    public int GlobalMaxActive { get; set; } = 15;

    // Per player per UTC calendar day, all types together (D15); null = no cap.
    public int? MaxClaimsPerPlayerPerDay { get; set; } = 10;

    public int AnnounceMinItemStars { get; set; } = 5;

    // 6 = spawn broadcasts are off while boxes are ★1-5 (Q3); set 5 to announce ★5 spawns.
    public int AnnounceSpawnMinBoxStars { get; set; } = 6;

    public string DropAnnouncementTemplate { get; set; } = DefaultDropAnnouncementTemplate;
    public string SpawnAnnouncementTemplate { get; set; } = DefaultSpawnAnnouncementTemplate;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public const string DefaultDropAnnouncementTemplate = "&6{player} &efound {item} &ein a {box}!";
    public const string DefaultSpawnAnnouncementTemplate = "&eA {box} &eappeared in &6{area}&e!";
}
