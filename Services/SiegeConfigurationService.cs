using System.Text.RegularExpressions;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services;

/// <summary>
/// The global siege tunables singleton (docs/specs/siege-minigame/DESIGN.md §3.8). Same pattern as
/// SalaryConfigurationService: the "global" row is created with the legacy defaults (the entity's
/// property initializers) on first read, and PUT edits it. Partial updates: null fields keep their
/// value. List settings are CSV columns exposed as arrays.
/// </summary>
public class SiegeConfigurationService : ISiegeConfigurationService
{
    private static readonly Regex EnchantmentKeyPattern = new("^[a-z0-9_.-]+:[a-z0-9_./-]+$", RegexOptions.Compiled);

    private readonly ISiegeConfigurationRepository _repository;

    public SiegeConfigurationService(ISiegeConfigurationRepository repository)
    {
        _repository = repository;
    }

    public async Task<SiegeConfigurationDto> GetAsync()
    {
        return ToDto(await EnsureExistsAsync());
    }

    public async Task<SiegeConfigurationDto> UpdateAsync(UpdateSiegeConfigurationDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        var config = await EnsureExistsAsync();

        // Apply, then validate the result as a whole (several rules compare two fields). Nothing is
        // saved when validation throws.
        config.CaptureAttackBase = dto.CaptureAttackBase ?? config.CaptureAttackBase;
        config.CaptureAttackPerExtra = dto.CaptureAttackPerExtra ?? config.CaptureAttackPerExtra;
        config.CaptureAttackPerExtraInstantVictory = dto.CaptureAttackPerExtraInstantVictory ?? config.CaptureAttackPerExtraInstantVictory;
        config.CaptureDefendBase = dto.CaptureDefendBase ?? config.CaptureDefendBase;
        config.CaptureDefendPerExtra = dto.CaptureDefendPerExtra ?? config.CaptureDefendPerExtra;
        config.CaptureDefendPerExtraInstantVictory = dto.CaptureDefendPerExtraInstantVictory ?? config.CaptureDefendPerExtraInstantVictory;
        config.SideCaptureReduction = dto.SideCaptureReduction ?? config.SideCaptureReduction;
        config.VoteCloseSecondsBeforeStart = dto.VoteCloseSecondsBeforeStart ?? config.VoteCloseSecondsBeforeStart;
        config.DrawSecondsBeforeStart = dto.DrawSecondsBeforeStart ?? config.DrawSecondsBeforeStart;
        config.HubSecondsBeforeStart = dto.HubSecondsBeforeStart ?? config.HubSecondsBeforeStart;
        config.TeamSplitSecondsBeforeStart = dto.TeamSplitSecondsBeforeStart ?? config.TeamSplitSecondsBeforeStart;
        config.KillStreakAnnounceAbove = dto.KillStreakAnnounceAbove ?? config.KillStreakAnnounceAbove;
        config.HeadshotMultiplier = dto.HeadshotMultiplier ?? config.HeadshotMultiplier;
        config.SpawnPickerDelayTicks = dto.SpawnPickerDelayTicks ?? config.SpawnPickerDelayTicks;
        config.EnchantDropChancePerMille = dto.EnchantDropChancePerMille ?? config.EnchantDropChancePerMille;
        config.EnchantLevelMin = dto.EnchantLevelMin ?? config.EnchantLevelMin;
        config.EnchantLevelMax = dto.EnchantLevelMax ?? config.EnchantLevelMax;
        config.MaxBooksAlive = dto.MaxBooksAlive ?? config.MaxBooksAlive;

        if (dto.NonMemberGateView.HasValue)
        {
            if (!Enum.IsDefined(dto.NonMemberGateView.Value))
                throw new ArgumentException($"Unknown nonMemberGateView '{dto.NonMemberGateView}'.");
            config.NonMemberGateView = dto.NonMemberGateView.Value;
        }

        if (dto.MatchmakingAnnouncementMarks != null)
            config.MatchmakingAnnouncementMarks = JoinPositiveInts(dto.MatchmakingAnnouncementMarks, "matchmakingAnnouncementMarks", descending: true);
        if (dto.KillAnnouncementThresholds != null)
            config.KillAnnouncementThresholds = JoinPositiveInts(dto.KillAnnouncementThresholds, "killAnnouncementThresholds", descending: false);
        if (dto.AllowedCommands != null)
            config.AllowedCommands = JoinCommands(dto.AllowedCommands);
        if (dto.AllowedEnchantmentKeys != null)
            config.AllowedEnchantmentKeys = JoinEnchantmentKeys(dto.AllowedEnchantmentKeys);

        Validate(config);
        await _repository.SaveAsync(config);
        return ToDto(config);
    }

    private async Task<SiegeConfiguration> EnsureExistsAsync()
    {
        var existing = await _repository.GetSingletonAsync();
        if (existing != null) return existing;

        // Seeded lazily with the legacy defaults (SalaryConfiguration precedent).
        var created = new SiegeConfiguration();
        await _repository.AddAsync(created);
        return created;
    }

    private static void Validate(SiegeConfiguration c)
    {
        if (new[] { c.CaptureAttackBase, c.CaptureAttackPerExtra, c.CaptureAttackPerExtraInstantVictory,
                    c.CaptureDefendBase, c.CaptureDefendPerExtra, c.CaptureDefendPerExtraInstantVictory }.Any(v => v < 0))
            throw new ArgumentException("Capture constants can't be negative.");
        if (c.SideCaptureReduction is < 0 or > 1)
            throw new ArgumentException("sideCaptureReduction must be between 0 and 1.");

        // The matchmaking timeline runs vote close -> draw -> hub -> team split -> start (§6.2–6.4).
        if (c.TeamSplitSecondsBeforeStart < 1
            || c.HubSecondsBeforeStart < c.TeamSplitSecondsBeforeStart
            || c.DrawSecondsBeforeStart < c.HubSecondsBeforeStart
            || c.VoteCloseSecondsBeforeStart < c.DrawSecondsBeforeStart)
            throw new ArgumentException(
                "Timeline offsets must satisfy voteClose ≥ draw ≥ hub ≥ teamSplit ≥ 1 (seconds before the match starts).");

        if (c.KillStreakAnnounceAbove < 0) throw new ArgumentException("killStreakAnnounceAbove can't be negative.");
        if (c.HeadshotMultiplier is < 1 or > 10) throw new ArgumentException("headshotMultiplier must be between 1.0 (off) and 10.");
        if (c.SpawnPickerDelayTicks < 0) throw new ArgumentException("spawnPickerDelayTicks can't be negative.");
        if (c.EnchantDropChancePerMille is < 0 or > 1000) throw new ArgumentException("enchantDropChancePerMille must be between 0 and 1000.");
        if (c.EnchantLevelMin < 1 || c.EnchantLevelMax < c.EnchantLevelMin || c.EnchantLevelMax > 255)
            throw new ArgumentException("Enchant levels must satisfy 1 ≤ enchantLevelMin ≤ enchantLevelMax ≤ 255.");
        if (c.MaxBooksAlive < 0) throw new ArgumentException("maxBooksAlive can't be negative.");
    }

    private static string JoinPositiveInts(List<int> values, string field, bool descending)
    {
        if (values.Any(v => v <= 0)) throw new ArgumentException($"{field} must contain positive numbers only.");
        var distinct = values.Distinct();
        distinct = descending ? distinct.OrderByDescending(v => v) : distinct.OrderBy(v => v);
        var joined = string.Join(",", distinct);
        if (joined.Length > 200) throw new ArgumentException($"{field} is too long.");
        return joined;
    }

    // "/siege", "siege " and "/SIEGE" all mean /siege.
    private static string JoinCommands(List<string> commands)
    {
        var normalized = commands
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim().ToLowerInvariant())
            .Select(c => c.StartsWith('/') ? c : "/" + c)
            .Distinct()
            .ToList();
        if (normalized.Any(c => c.Length < 2 || c.Contains(' ') || c.Contains(',')))
            throw new ArgumentException("allowedCommands must be single command names such as /siege or /msg.");
        var joined = string.Join(",", normalized);
        if (joined.Length > 1000) throw new ArgumentException("allowedCommands is too long.");
        return joined;
    }

    // "sharpness" -> "minecraft:sharpness".
    private static string JoinEnchantmentKeys(List<string> keys)
    {
        var normalized = keys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim().ToLowerInvariant())
            .Select(k => k.Contains(':') ? k : "minecraft:" + k)
            .Distinct()
            .ToList();
        var bad = normalized.FirstOrDefault(k => !EnchantmentKeyPattern.IsMatch(k));
        if (bad != null) throw new ArgumentException($"'{bad}' isn't an enchantment key such as minecraft:sharpness.");
        var joined = string.Join(",", normalized);
        if (joined.Length > 2000) throw new ArgumentException("allowedEnchantmentKeys is too long.");
        return joined;
    }

    internal static List<int> SplitInts(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? v : (int?)null)
            .Where(v => v.HasValue).Select(v => v!.Value)
            .ToList();

    internal static List<string> SplitStrings(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public static SiegeConfigurationDto ToDto(SiegeConfiguration c) => new()
    {
        CaptureAttackBase = c.CaptureAttackBase,
        CaptureAttackPerExtra = c.CaptureAttackPerExtra,
        CaptureAttackPerExtraInstantVictory = c.CaptureAttackPerExtraInstantVictory,
        CaptureDefendBase = c.CaptureDefendBase,
        CaptureDefendPerExtra = c.CaptureDefendPerExtra,
        CaptureDefendPerExtraInstantVictory = c.CaptureDefendPerExtraInstantVictory,
        SideCaptureReduction = c.SideCaptureReduction,
        VoteCloseSecondsBeforeStart = c.VoteCloseSecondsBeforeStart,
        DrawSecondsBeforeStart = c.DrawSecondsBeforeStart,
        HubSecondsBeforeStart = c.HubSecondsBeforeStart,
        TeamSplitSecondsBeforeStart = c.TeamSplitSecondsBeforeStart,
        MatchmakingAnnouncementMarks = SplitInts(c.MatchmakingAnnouncementMarks),
        KillAnnouncementThresholds = SplitInts(c.KillAnnouncementThresholds),
        KillStreakAnnounceAbove = c.KillStreakAnnounceAbove,
        HeadshotMultiplier = c.HeadshotMultiplier,
        AllowedCommands = SplitStrings(c.AllowedCommands),
        SpawnPickerDelayTicks = c.SpawnPickerDelayTicks,
        EnchantDropChancePerMille = c.EnchantDropChancePerMille,
        AllowedEnchantmentKeys = SplitStrings(c.AllowedEnchantmentKeys),
        EnchantLevelMin = c.EnchantLevelMin,
        EnchantLevelMax = c.EnchantLevelMax,
        MaxBooksAlive = c.MaxBooksAlive,
        NonMemberGateView = c.NonMemberGateView,
        UpdatedAt = DateTime.SpecifyKind(c.UpdatedAt, DateTimeKind.Utc)
    };
}
