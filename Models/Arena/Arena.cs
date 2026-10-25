using System.Collections.Generic;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// A duel arena (arena duels DESIGN.md §4.1, D12): a Structure whose WgRegionId (and KNG-253 sub-regions) is the whole
/// arena, a domain with normal domain behaviour. Its battlegrounds, spectator areas and lobby are its own
/// <see cref="ArenaArea"/>s (rev. 6), not domains. The duel settings (§4.2) carry the design's defaults, so a new arena
/// works untouched. ArenaService validates them.
/// </summary>
[FormConfigurableEntity("Arena")]
public class Arena : Structure
{
    /// <summary>Stable, unique name for commands and config (e.g. "arena_2").</summary>
    public string Key { get; set; } = null!;

    /// <summary>Where duelists go after the match; null = the domain's own Location.</summary>
    [NavigationPair(nameof(ExitLocation))]
    [RelatedEntityField(typeof(Location))]
    public int? ExitLocationId { get; set; }
    [RelatedEntityField(typeof(Location))]
    public Location? ExitLocation { get; set; }

    // ===== General =====
    /// <summary>Needs an enabled LOBBY area with an arrival location (DESIGN §4.1).</summary>
    public bool IsDuelEnabled { get; set; } = true;
    /// <summary>Accepted matches not yet in a battleground (travelling, warmup, waiting).</summary>
    public int MaxQueuedMatches { get; set; } = 3;

    // ===== Negotiation =====
    /// <summary>Offers per negotiation, 1–6 (D2).</summary>
    public int MaxOffers { get; set; } = 3;
    /// <summary>Seconds per offer turn.</summary>
    public int OfferSeconds { get; set; } = 60;

    // ===== Stakes =====
    public bool AllowCoinStakes { get; set; } = true;
    public bool AllowItemStakes { get; set; } = true;
    /// <summary>Per side.</summary>
    public int MaxCoinStake { get; set; } = 10_000;
    /// <summary>Per side.</summary>
    public int MaxItemStacks { get; set; } = 9;
    /// <summary>The smaller coin stake must be at least this % of the larger; 0 = off.</summary>
    public int MinStakeRatioPercent { get; set; } = 50;
    /// <summary>Fee on the coin pot of a decisive result, 0–20 (D15).</summary>
    public int FeePercent { get; set; } = 5;

    // ===== Eligibility (D14) =====
    /// <summary>null = CurrencyPolicy.MinSenderAccountAgeHours.</summary>
    public int? StakeMinAccountAgeHours { get; set; }
    /// <summary>null = the /pay minimum title.</summary>
    [NavigationPair(nameof(StakeMinTitleBracket))]
    [RelatedEntityField(typeof(TitleBracket))]
    public int? StakeMinTitleBracketId { get; set; }
    [RelatedEntityField(typeof(TitleBracket))]
    public TitleBracket? StakeMinTitleBracket { get; set; }

    // ===== Abuse limits (§10.2) =====
    public int MaxStakedDuelsPerPairPerDay { get; set; } = 3;
    /// <summary>Between staked duels of the same two players.</summary>
    public int PairCooldownMinutes { get; set; } = 10;
    /// <summary>Per player per day; 0 = off.</summary>
    public int DailyCoinWinCap { get; set; } = 25_000;

    // ===== Gear (D9) =====
    public bool AllowOwnGearKeep { get; set; } = true;
    public bool AllowOwnGearDrop { get; set; } = false;
    public bool AllowFixedKit { get; set; } = false;
    public bool AllowKitChoice { get; set; } = false;
    /// <summary>Must be one of the allowed modes.</summary>
    public DuelGearMode DefaultGearMode { get; set; } = DuelGearMode.OWN_GEAR_KEEP;

    // ===== Timing =====
    public int WarmupSeconds { get; set; } = 30;
    public int CountdownSeconds { get; set; } = 10;
    /// <summary>0 = no limit (D11).</summary>
    public int FightTimeLimitSeconds { get; set; } = 300;
    /// <summary>Waiting for a free battleground, then cancel and refund.</summary>
    public int MaxWaitSeconds { get; set; } = 180;
    public int WinnerLingerSeconds { get; set; } = 5;

    // ===== Announcements =====
    public bool AnnounceMatches { get; set; } = true;
    /// <summary>Also announce duels without stakes.</summary>
    public bool AnnounceFriendly { get; set; } = true;

    // ===== Children =====
    /// <summary>Battlegrounds, spectator areas and the lobby (rev. 6), each with its own regions.</summary>
    [RelatedEntityField(typeof(ArenaArea))]
    public ICollection<ArenaArea> Areas { get; set; } = new List<ArenaArea>();

    /// <summary>Ordered; the first is the default pick in KIT_CHOICE. Edited as a replace-set with the arena.</summary>
    [RelatedEntityField(typeof(ArenaKit))]
    public ICollection<ArenaKit> EligibleKits { get; set; } = new List<ArenaKit>();
}
