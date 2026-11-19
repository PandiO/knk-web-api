using System;
using System.Collections.Generic;
using System.Linq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services.Territory
{
    /// <summary>A holder as a (kind, id) pair, comparable so a conflict pair has one normal order.</summary>
    public readonly record struct TerritoryHolder(TerritoryHolderKind Kind, int Id) : IComparable<TerritoryHolder>
    {
        public int CompareTo(TerritoryHolder other)
        {
            int byKind = Kind.CompareTo(other.Kind);
            return byKind != 0 ? byKind : Id.CompareTo(other.Id);
        }

        public override string ToString() => $"{Kind} {Id}";
    }

    /// <summary>What owns a position, given every holder whose range reaches it (<see cref="TerritoryRules.Resolve"/>).</summary>
    public sealed record TerritoryVerdict(TerritoryHolder? Owner, IReadOnlyList<TerritoryHolder> Contesting)
    {
        public bool IsContested => Contesting.Count > 1;
        public bool IsWilderness => Owner == null && Contesting.Count == 0;
    }

    /// <summary>
    /// The territory rules of the API side (territory DESIGN § 1-3, decisions G10, G11), pure so they are tested without
    /// a database. The game server applies the same rules to positions in its chunk cache; <see cref="Resolve"/> is the
    /// reference for that.
    /// </summary>
    public static class TerritoryRules
    {
        /// <summary>Crown land (held by a kingdom) outranks towns (territory DESIGN § 2).</summary>
        public static int Rank(TerritoryHolderKind kind) => kind switch
        {
            TerritoryHolderKind.Kingdom => 2,
            _ => 1,
        };

        /// <summary>A town's normal district range: base range + power × factor, never below 0.</summary>
        public static double TownRange(TerritorySettingsDto settings, int power) =>
            Math.Max(0, settings.BaseRange + power * settings.PowerFactor);

        /// <summary>G11: a capital district's range is the town range multiplied by the capital multiplier.</summary>
        public static double DistrictRange(TerritorySettingsDto settings, int power, bool isCapital) =>
            isCapital ? TownRange(settings, power) * settings.CapitalMultiplier : TownRange(settings, power);

        /// <summary>The normal order of a pair: the smaller (kind, id) first.</summary>
        public static (TerritoryHolder A, TerritoryHolder B) Normalize(TerritoryHolder a, TerritoryHolder b) =>
            a.CompareTo(b) <= 0 ? (a, b) : (b, a);

        /// <summary>An overlap of these two is a conflict: different holders of equal rank. A town against crown land is
        /// not (crown land wins), nor is a holder against itself (two districts of one town, two crown lands of one
        /// kingdom).</summary>
        public static bool IsConflictPair(TerritoryHolder a, TerritoryHolder b) =>
            a != b && Rank(a.Kind) == Rank(b.Kind);

        /// <summary>
        /// The owner of a position reached by <paramref name="claims"/> (every holder whose range reaches it): the holders
        /// of the highest rank; one of them owns it, two or more contest it (no owner) unless
        /// <paramref name="awardedTo"/> (an admin's award covering the position) is one of them.
        /// </summary>
        public static TerritoryVerdict Resolve(IEnumerable<TerritoryHolder> claims, TerritoryHolder? awardedTo = null)
        {
            var distinct = claims.Distinct().ToList();
            if (distinct.Count == 0) return new TerritoryVerdict(null, Array.Empty<TerritoryHolder>());
            int top = distinct.Max(h => Rank(h.Kind));
            var best = distinct.Where(h => Rank(h.Kind) == top).OrderBy(h => h).ToList();
            if (best.Count == 1) return new TerritoryVerdict(best[0], best);
            if (awardedTo is { } award && best.Contains(award)) return new TerritoryVerdict(award, best);
            return new TerritoryVerdict(null, best);
        }

        public static TerritoryHolderKind ParseKind(string? kind) =>
            Enum.TryParse<TerritoryHolderKind>(kind?.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
                ? parsed
                : throw new ArgumentException($"Unknown territory holder kind '{kind}' (Town or Kingdom).");

        public static TerritoryHolder Parse(TerritoryHolderDto? dto, string what) =>
            dto == null
                ? throw new ArgumentException($"{what} is required.")
                : dto.Id <= 0
                    ? throw new ArgumentException($"{what} needs an id.")
                    : new TerritoryHolder(ParseKind(dto.Kind), dto.Id);

        /// <summary>Validates and normalises the range settings; throws <see cref="ArgumentException"/> when out of bounds.</summary>
        public static TerritorySettingsDto Validate(TerritorySettingsDto? settings)
        {
            var s = settings ?? new TerritorySettingsDto();
            if (s.BaseRange < 0 || s.BaseRange > TerritorySettingsDto.MaxBaseRange)
                throw new ArgumentException($"production.territory.baseRange must be between 0 and {TerritorySettingsDto.MaxBaseRange}.");
            if (double.IsNaN(s.PowerFactor) || s.PowerFactor < 0 || s.PowerFactor > TerritorySettingsDto.MaxPowerFactor)
                throw new ArgumentException($"production.territory.powerFactor must be between 0 and {TerritorySettingsDto.MaxPowerFactor}.");
            if (double.IsNaN(s.CapitalMultiplier) || s.CapitalMultiplier < 1 || s.CapitalMultiplier > TerritorySettingsDto.MaxCapitalMultiplier)
                throw new ArgumentException($"production.territory.capitalMultiplier must be between 1 and {TerritorySettingsDto.MaxCapitalMultiplier}.");
            return new TerritorySettingsDto { BaseRange = s.BaseRange, PowerFactor = s.PowerFactor, CapitalMultiplier = s.CapitalMultiplier };
        }

        /// <summary>The chunk list, distinct and sorted, with its bounding box. Throws on a malformed entry.</summary>
        public static (List<int[]> Chunks, int MinX, int MinZ, int MaxX, int MaxZ) NormalizeChunks(IEnumerable<int[]>? chunks)
        {
            var set = new SortedSet<(int X, int Z)>();
            foreach (var c in chunks ?? Enumerable.Empty<int[]>())
            {
                if (c == null || c.Length != 2) throw new ArgumentException("Every chunk is an [x, z] pair.");
                set.Add((c[0], c[1]));
            }
            if (set.Count == 0) return (new List<int[]>(), 0, 0, 0, 0);
            return (set.Select(p => new[] { p.X, p.Z }).ToList(),
                set.Min(p => p.X), set.Min(p => p.Z), set.Max(p => p.X), set.Max(p => p.Z));
        }
    }
}
