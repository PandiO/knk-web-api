using System;
using System.Collections.Generic;
using System.Linq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services
{
    /// <summary>The kinds of teleport a permission group can price (Linear KNG-41).</summary>
    public enum TeleportFeeKind
    {
        /// <summary>/tpa and /tpahere, paid by the requester.</summary>
        Request,
        /// <summary>/warp and the teleport menu.</summary>
        Warp,
        /// <summary>/spawn.</summary>
        Spawn
    }

    /// <summary>One kind's price/cooldown settings as stored on a group.</summary>
    public sealed record TeleportKindSettings(TeleportPriceMode Mode, decimal? Multiplier, int? Coins, int? Gems,
        int? Experience, int? CooldownSeconds);

    /// <summary>One currency of a price; Amount ≥ 1.</summary>
    public sealed record TeleportPriceLeg(Currency Currency, long Amount);

    /// <summary>
    /// A player's resolved settings for one kind: the first group (in <see cref="TeleportGroupPolicy.Chain"/>
    /// order) that sets a price, and the first that sets a cooldown — each null when no group does.
    /// </summary>
    public sealed record TeleportKindPolicy(TeleportFeeKind Kind, PermissionGroup? PriceGroup, TeleportKindSettings? Price,
        PermissionGroup? CooldownGroup, int? CooldownSeconds)
    {
        public static TeleportKindPolicy None(TeleportFeeKind kind) => new(kind, null, null, null, null);

        /// <summary>
        /// What this kind costs given its default price (<paramref name="defaultLegs"/>: the request's
        /// coin fee, the domain's gems, nothing for /spawn): the default when no group sets a price,
        /// the default times the multiplier (rounded half away from zero, capped at the balance cap),
        /// or the group's fixed price. Zero amounts are left out, so an empty list means free.
        /// </summary>
        public IReadOnlyList<TeleportPriceLeg> PriceFor(IReadOnlyList<TeleportPriceLeg> defaultLegs)
        {
            IEnumerable<TeleportPriceLeg> legs = Price?.Mode switch
            {
                TeleportPriceMode.Fixed => new[]
                {
                    new TeleportPriceLeg(Currency.Coins, Price.Coins ?? 0),
                    new TeleportPriceLeg(Currency.Gems, Price.Gems ?? 0),
                    new TeleportPriceLeg(Currency.Experience, Price.Experience ?? 0)
                },
                TeleportPriceMode.Multiplier => defaultLegs.Select(l => l with
                {
                    Amount = Math.Min(Cap(l.Currency),
                        (long)Math.Round(l.Amount * (Price.Multiplier ?? 1m), MidpointRounding.AwayFromZero))
                }),
                _ => defaultLegs
            };
            return legs.Where(l => l.Amount > 0).ToList();
        }

        private static long Cap(Currency currency) => currency switch
        {
            Currency.Coins => BalanceLimits.MaxCoins,
            Currency.Gems => BalanceLimits.MaxGems,
            _ => BalanceLimits.MaxExperience
        };
    }

    /// <summary>
    /// Resolves a player's per-group teleport fees and cooldowns (Linear KNG-41). The player's
    /// groups are checked from the highest Weight down (ties by id), each followed by its parent
    /// chain before the next group — the order permission grants resolve in
    /// (PermissionResolutionService) — and the first group that sets a value wins. Price and
    /// cooldown are resolved separately, so a premium tier can shorten the cooldown while the rank
    /// below it keeps setting the price.
    /// </summary>
    public static class TeleportGroupPolicy
    {
        /// <summary>
        /// The groups to check, in order. <paramref name="memberGroups"/> are the player's active
        /// groups with ParentGroup loaded (IPermissionGroupRepository.GetActiveGroupsForUserAsync).
        /// A group reached twice (a shared parent) is checked at its first position only.
        /// </summary>
        public static List<PermissionGroup> Chain(IEnumerable<PermissionGroup> memberGroups)
        {
            var chain = new List<PermissionGroup>();
            var seen = new HashSet<int>();
            foreach (var group in memberGroups.OrderByDescending(g => g.Weight).ThenBy(g => g.Id))
            {
                var current = group;
                var walked = new HashSet<int>();
                while (current != null && walked.Add(current.Id))
                {
                    if (seen.Add(current.Id))
                    {
                        chain.Add(current);
                    }
                    current = current.ParentGroup;
                }
            }
            return chain;
        }

        public static TeleportKindPolicy Resolve(IReadOnlyList<PermissionGroup> chain, TeleportFeeKind kind)
        {
            PermissionGroup? priceGroup = null;
            TeleportKindSettings? price = null;
            PermissionGroup? cooldownGroup = null;
            int? cooldown = null;
            foreach (var group in chain)
            {
                var settings = SettingsOf(group, kind);
                if (price == null && settings.Mode != TeleportPriceMode.None)
                {
                    priceGroup = group;
                    price = settings;
                }
                if (cooldown == null && settings.CooldownSeconds != null)
                {
                    cooldownGroup = group;
                    cooldown = settings.CooldownSeconds;
                }
                if (price != null && cooldown != null) break;
            }
            return new TeleportKindPolicy(kind, priceGroup, price, cooldownGroup, cooldown);
        }

        public static TeleportKindSettings SettingsOf(PermissionGroup group, TeleportFeeKind kind) => kind switch
        {
            TeleportFeeKind.Request => new(group.TeleportRequestPriceMode, group.TeleportRequestPriceMultiplier,
                group.TeleportRequestPriceCoins, group.TeleportRequestPriceGems, group.TeleportRequestPriceExperience,
                group.TeleportRequestCooldownSeconds),
            TeleportFeeKind.Warp => new(group.TeleportWarpPriceMode, group.TeleportWarpPriceMultiplier,
                group.TeleportWarpPriceCoins, group.TeleportWarpPriceGems, group.TeleportWarpPriceExperience,
                group.TeleportWarpCooldownSeconds),
            _ => new(group.TeleportSpawnPriceMode, null, group.TeleportSpawnPriceCoins, group.TeleportSpawnPriceGems,
                group.TeleportSpawnPriceExperience, group.TeleportSpawnCooldownSeconds)
        };

        public static TeleportKindPolicyDto ToDto(TeleportKindPolicy policy) => new()
        {
            PriceMode = policy.Price?.Mode ?? TeleportPriceMode.None,
            PriceMultiplier = policy.Price?.Mode == TeleportPriceMode.Multiplier ? policy.Price.Multiplier : null,
            PriceCoins = policy.Price?.Mode == TeleportPriceMode.Fixed ? policy.Price.Coins ?? 0 : null,
            PriceGems = policy.Price?.Mode == TeleportPriceMode.Fixed ? policy.Price.Gems ?? 0 : null,
            PriceExperience = policy.Price?.Mode == TeleportPriceMode.Fixed ? policy.Price.Experience ?? 0 : null,
            PriceGroupName = policy.PriceGroup?.Name,
            CooldownSeconds = policy.CooldownSeconds,
            CooldownGroupName = policy.CooldownGroup?.Name
        };
    }

    /// <summary>
    /// The teleport fields of a PermissionGroup DTO (KNG-41): validation and applying them to the
    /// entity. Each kind's PriceMode is its switch — omitted leaves the kind's stored fields alone.
    /// </summary>
    public static class PermissionGroupTeleportSettings
    {
        /// <summary>Highest group cooldown: a day.</summary>
        public const int MaxCooldownSeconds = 86_400;

        /// <summary>Highest multiplier.</summary>
        public const decimal MaxMultiplier = 1000m;

        public static void Validate(PermissionGroupDto dto)
        {
            ValidateKind("/tpa", dto.TeleportRequestPriceMode, dto.TeleportRequestPriceMultiplier, dto.TeleportRequestPriceCoins,
                dto.TeleportRequestPriceGems, dto.TeleportRequestPriceExperience, dto.TeleportRequestCooldownSeconds, allowMultiplier: true);
            ValidateKind("/warp", dto.TeleportWarpPriceMode, dto.TeleportWarpPriceMultiplier, dto.TeleportWarpPriceCoins,
                dto.TeleportWarpPriceGems, dto.TeleportWarpPriceExperience, dto.TeleportWarpCooldownSeconds, allowMultiplier: true);
            ValidateKind("/spawn", dto.TeleportSpawnPriceMode, null, dto.TeleportSpawnPriceCoins,
                dto.TeleportSpawnPriceGems, dto.TeleportSpawnPriceExperience, dto.TeleportSpawnCooldownSeconds, allowMultiplier: false);
        }

        private static void ValidateKind(string label, TeleportPriceMode? mode, decimal? multiplier, int? coins, int? gems,
            int? experience, int? cooldown, bool allowMultiplier)
        {
            if (mode == null) return;
            if (!Enum.IsDefined(mode.Value))
            {
                throw new ArgumentException($"Unknown {label} teleport price mode.");
            }
            if (mode == TeleportPriceMode.Multiplier)
            {
                if (!allowMultiplier)
                {
                    throw new ArgumentException($"{label} has no default price to multiply; use a fixed price.");
                }
                if (multiplier == null)
                {
                    throw new ArgumentException($"The {label} price multiplier is required for the Multiplier price mode.");
                }
            }
            if (multiplier is < 0 or > MaxMultiplier)
            {
                throw new ArgumentException($"The {label} price multiplier must be between 0 and {MaxMultiplier}.");
            }
            if (coins is < 0 or > BalanceLimits.MaxCoins)
            {
                throw new ArgumentException($"The {label} price in coins must be between 0 and {BalanceLimits.MaxCoins}.");
            }
            if (gems is < 0 or > BalanceLimits.MaxGems)
            {
                throw new ArgumentException($"The {label} price in gems must be between 0 and {BalanceLimits.MaxGems}.");
            }
            if (experience is < 0)
            {
                throw new ArgumentException($"The {label} price in XP can't be negative.");
            }
            if (cooldown is < 0 or > MaxCooldownSeconds)
            {
                throw new ArgumentException($"The {label} cooldown must be between 0 and {MaxCooldownSeconds} seconds.");
            }
        }

        /// <summary>Copies each kind whose PriceMode the DTO carries onto <paramref name="target"/>.</summary>
        public static void Apply(PermissionGroup target, PermissionGroupDto dto)
        {
            if (dto.TeleportRequestPriceMode is { } request)
            {
                target.TeleportRequestPriceMode = request;
                target.TeleportRequestPriceMultiplier = dto.TeleportRequestPriceMultiplier;
                target.TeleportRequestPriceCoins = dto.TeleportRequestPriceCoins;
                target.TeleportRequestPriceGems = dto.TeleportRequestPriceGems;
                target.TeleportRequestPriceExperience = dto.TeleportRequestPriceExperience;
                target.TeleportRequestCooldownSeconds = dto.TeleportRequestCooldownSeconds;
            }
            if (dto.TeleportWarpPriceMode is { } warp)
            {
                target.TeleportWarpPriceMode = warp;
                target.TeleportWarpPriceMultiplier = dto.TeleportWarpPriceMultiplier;
                target.TeleportWarpPriceCoins = dto.TeleportWarpPriceCoins;
                target.TeleportWarpPriceGems = dto.TeleportWarpPriceGems;
                target.TeleportWarpPriceExperience = dto.TeleportWarpPriceExperience;
                target.TeleportWarpCooldownSeconds = dto.TeleportWarpCooldownSeconds;
            }
            if (dto.TeleportSpawnPriceMode is { } spawn)
            {
                target.TeleportSpawnPriceMode = spawn;
                target.TeleportSpawnPriceCoins = dto.TeleportSpawnPriceCoins;
                target.TeleportSpawnPriceGems = dto.TeleportSpawnPriceGems;
                target.TeleportSpawnPriceExperience = dto.TeleportSpawnPriceExperience;
                target.TeleportSpawnCooldownSeconds = dto.TeleportSpawnCooldownSeconds;
            }
        }

        /// <summary>The DTO properties <see cref="Apply"/> owns (AutoMapper ignores them on DTO → entity).</summary>
        public static readonly string[] EntityProperties =
        {
            nameof(PermissionGroup.TeleportRequestPriceMode), nameof(PermissionGroup.TeleportRequestPriceMultiplier),
            nameof(PermissionGroup.TeleportRequestPriceCoins), nameof(PermissionGroup.TeleportRequestPriceGems),
            nameof(PermissionGroup.TeleportRequestPriceExperience), nameof(PermissionGroup.TeleportRequestCooldownSeconds),
            nameof(PermissionGroup.TeleportWarpPriceMode), nameof(PermissionGroup.TeleportWarpPriceMultiplier),
            nameof(PermissionGroup.TeleportWarpPriceCoins), nameof(PermissionGroup.TeleportWarpPriceGems),
            nameof(PermissionGroup.TeleportWarpPriceExperience), nameof(PermissionGroup.TeleportWarpCooldownSeconds),
            nameof(PermissionGroup.TeleportSpawnPriceMode), nameof(PermissionGroup.TeleportSpawnPriceCoins),
            nameof(PermissionGroup.TeleportSpawnPriceGems), nameof(PermissionGroup.TeleportSpawnPriceExperience),
            nameof(PermissionGroup.TeleportSpawnCooldownSeconds)
        };
    }
}
