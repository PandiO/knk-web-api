using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Domain discovery reward math (docs/specs/domain-discovery/DESIGN.md §3.3): v1's title-scaled
/// XP (getExpPart), coins in hours of the title's Salary, flat gems, and KNG-16 multipliers.
/// </summary>
public class DiscoveryRewardCalculatorTests
{
    /// <summary>The real v3 title data (AddUserFeaturesPhase6RealTitleDataAndFreeze). The in-memory
    /// database treats Id 0 as "generate one", so tests that store them shift the ids.</summary>
    internal static List<TitleBracket> RealBrackets(int idOffset = 0) => new List<TitleBracket>
    {
        B(0, "Serf", 0, 650, 0, 0, 0),
        B(1, "Peasant", 2500, 1350, 13500, 3, 32),
        B(2, "Yeoman", 3200, 1650, 16500, 3, 40),
        B(3, "Squire", 4000, 2000, 20000, 4, 45),
        B(4, "Reeve", 4500, 2400, 24000, 4, 100),
        B(5, "Knight", 10000, 4800, 48000, 50, 115),
        B(6, "Baronet", 11500, 5750, 57500, 5, 130),
        B(7, "Baron", 13000, 7000, 70000, 5, 165),
        B(8, "Viscount", 16500, 8250, 82500, 5, 200),
        B(9, "Count", 20000, 10000, 100000, 5, 250),
        B(10, "Margrave", 30000, 15000, 150000, 100, 300),
        B(11, "Duke", 35500, 18000, 180000, 6, 350),
        B(12, "Grand Duke", 42500, 21500, 215000, 50, 375),
        B(13, "Regent", 50000, 25750, 257500, 10, 400),
        B(14, "Prince Consort", 60000, 30850, 308500, 10, 500),
        B(15, "Prince", 92500, 46300, 463000, 50, 600),
        B(16, "King", 110000, 55555, 555550, 20, 750),
        B(17, "Emperor", 130000, 66666, 666660, 20, 850),
        B(18, "One of the Seven", 160000, 80000, 800000, 250, 1200),
    }.Select(b => { b.Id += idOffset; return b; }).ToList();

    private static TitleBracket B(int id, string name, int minExp, int salary, int coinBonus, int gemBonus, int expBonus) => new()
    {
        Id = id, MaleName = name, FemaleName = name, MinExperience = minExp, Salary = salary,
        CoinBonus = coinBonus, GemBonus = gemBonus, ExpBonus = expBonus
    };

    /// <summary>The migration's seeded rules.</summary>
    internal static DiscoveryRewardRule TownRule() => new()
    {
        DomainType = "Town", IsEnabled = true, ExpUnitsMin = 1m, ExpUnitsMax = 4m,
        CoinSalaryHoursMin = 2m, CoinSalaryHoursMax = 8m, GemsMin = 5, GemsMax = 15, IncludeAncestors = false
    };

    internal static DiscoveryRewardRule DistrictRule() => new()
    {
        DomainType = "District", IsEnabled = true, ExpUnitsMin = 0.5m, ExpUnitsMax = 2m,
        CoinSalaryHoursMin = 0.5m, CoinSalaryHoursMax = 2m, GemsMin = 1, GemsMax = 3, IncludeAncestors = true
    };

    internal static DiscoveryRewardRule StructureRule(string type = "Structure") => new()
    {
        DomainType = type, IsEnabled = true, ExpUnitsMin = 0.05m, ExpUnitsMax = 0.25m,
        CoinSalaryHoursMin = 0.05m, CoinSalaryHoursMax = 0.25m, GemsMin = 0, GemsMax = 0, IncludeAncestors = true
    };

    /// <summary>A Random whose rolls are fixed: NextDouble returns <c>fraction</c>, Next(lo, hi)
    /// the same fraction of its range.</summary>
    internal sealed class FixedRandom : Random
    {
        private readonly double _fraction;
        public FixedRandom(double fraction) { _fraction = fraction; }
        public override double NextDouble() => _fraction;
        public override int Next(int minValue, int maxValue) =>
            Math.Min(maxValue - 1, minValue + (int)Math.Floor((maxValue - minValue) * _fraction));
    }

    private static (int ExpUnit, int Salary) At(string title)
    {
        var brackets = RealBrackets();
        var bracket = brackets.Single(b => b.MaleName == title);
        return (DiscoveryRewardCalculator.ExpUnit(brackets, bracket), DiscoveryRewardCalculator.SalaryOf(bracket));
    }

    [Theory]
    [InlineData("Serf", 25, 100)]      // v1 parity: bracket width 2,500 -> unit 25
    [InlineData("Peasant", 7, 28)]
    [InlineData("Reeve", 55, 220)]
    [InlineData("Knight", 15, 60)]     // narrower than Reeve - inherited from v1's uneven brackets
    [InlineData("Count", 100, 400)]
    [InlineData("Prince", 175, 700)]
    public void TownXp_IsV1sOneToFourPercentOfTheTitleBracket(string title, int min, int max)
    {
        var (unit, salary) = At(title);

        Assert.Equal(min, DiscoveryRewardCalculator.Min(TownRule(), unit, salary).Exp);
        Assert.Equal(max, DiscoveryRewardCalculator.Max(TownRule(), unit, salary).Exp);
    }

    [Fact]
    public void TopBracket_UsesTheWidthOfTheBracketBelow()
    {
        // One of the Seven has no next bracket (v3 dropped MaxExp): Emperor -> OotS is 30,000 wide.
        var (unit, salary) = At("One of the Seven");

        Assert.Equal(300, unit);
        Assert.Equal((300, 1200), (DiscoveryRewardCalculator.Min(TownRule(), unit, salary).Exp, DiscoveryRewardCalculator.Max(TownRule(), unit, salary).Exp));
        Assert.Equal((160000, 640000), (DiscoveryRewardCalculator.Min(TownRule(), unit, salary).Coins, DiscoveryRewardCalculator.Max(TownRule(), unit, salary).Coins));
    }

    [Fact]
    public void NoBrackets_FallsBackToSerf()
    {
        var empty = new List<TitleBracket>();
        var bracket = DiscoveryRewardCalculator.CurrentBracket(empty, 5000);

        Assert.Null(bracket);
        Assert.Equal(25, DiscoveryRewardCalculator.ExpUnit(empty, bracket));
        Assert.Equal(650, DiscoveryRewardCalculator.SalaryOf(bracket));
    }

    [Fact]
    public void SingleBracket_HasNoWidthSoUsesTheFallbackUnit()
    {
        var one = new List<TitleBracket> { B(0, "Serf", 0, 650, 0, 0, 0) };

        Assert.Equal(25, DiscoveryRewardCalculator.ExpUnit(one, one[0]));
    }

    [Theory]
    [InlineData(0, "Serf")]
    [InlineData(2499, "Serf")]
    [InlineData(2500, "Peasant")]
    [InlineData(999999, "One of the Seven")]
    public void CurrentBracket_IsTheHighestReached(int xp, string title)
    {
        Assert.Equal(title, DiscoveryRewardCalculator.CurrentBracket(RealBrackets(), xp)!.MaleName);
    }

    [Theory]
    [InlineData("Serf", 1300, 5200)]
    [InlineData("Knight", 9600, 38400)]
    [InlineData("Count", 20000, 80000)]
    public void TownCoins_AreTwoToEightHoursOfTheTitlesSalary(string title, int min, int max)
    {
        var (unit, salary) = At(title);

        Assert.Equal(min, DiscoveryRewardCalculator.Min(TownRule(), unit, salary).Coins);
        Assert.Equal(max, DiscoveryRewardCalculator.Max(TownRule(), unit, salary).Coins);
    }

    [Theory]
    [InlineData("Serf")]
    [InlineData("One of the Seven")]
    public void Gems_AreFlatWhateverTheTitle(string title)
    {
        var (unit, salary) = At(title);

        Assert.Equal(5, DiscoveryRewardCalculator.Min(TownRule(), unit, salary).Gems);
        Assert.Equal(15, DiscoveryRewardCalculator.Max(TownRule(), unit, salary).Gems);
    }

    [Fact]
    public void Structures_AreSmallAndGemless()
    {
        var (unit, salary) = At("Serf");

        Assert.Equal(new DiscoveryReward(33, 0, 1), DiscoveryRewardCalculator.Min(StructureRule(), unit, salary)); // 32.5 -> 33, 1.25 -> 1
        Assert.Equal(new DiscoveryReward(163, 0, 6), DiscoveryRewardCalculator.Max(StructureRule(), unit, salary)); // 162.5 -> 163, 6.25 -> 6
    }

    [Fact]
    public void Roll_InterpolatesBetweenMinAndMax()
    {
        var (unit, salary) = At("Serf");

        Assert.Equal(DiscoveryRewardCalculator.Min(TownRule(), unit, salary), DiscoveryRewardCalculator.Roll(TownRule(), unit, salary, new FixedRandom(0)));
        // Midpoint: 2.5 units x 25 = 62.5 -> 63 (away from zero), 5h x 650, gems 5 + floor(11 x 0.5) = 10.
        Assert.Equal(new DiscoveryReward(3250, 10, 63), DiscoveryRewardCalculator.Roll(TownRule(), unit, salary, new FixedRandom(0.5)));
        Assert.Equal(new DiscoveryReward(5200, 15, 100), DiscoveryRewardCalculator.Roll(TownRule(), unit, salary, new FixedRandom(0.99999999)));
    }

    [Fact]
    public void Roll_StaysInRangeOverManyRolls()
    {
        var (unit, salary) = At("Knight");
        var random = new Random(42);
        for (var i = 0; i < 1000; i++)
        {
            var roll = DiscoveryRewardCalculator.Roll(DistrictRule(), unit, salary, random);
            Assert.InRange(roll.Exp, 8, 30);      // 0.5-2 x 15
            Assert.InRange(roll.Coins, 2400, 9600); // 0.5-2 h x 4,800
            Assert.InRange(roll.Gems, 1, 3);
        }
    }

    [Fact]
    public void MinAboveMaxOrNegative_IsOrderedAndFlooredInsteadOfRollingNonsense()
    {
        var broken = new DiscoveryRewardRule
        {
            DomainType = "Town", IsEnabled = true, ExpUnitsMin = 4m, ExpUnitsMax = 1m,
            CoinSalaryHoursMin = -3m, CoinSalaryHoursMax = -1m, GemsMin = 15, GemsMax = 5
        };

        Assert.Equal(new DiscoveryReward(0, 5, 25), DiscoveryRewardCalculator.Min(broken, 25, 650));
        Assert.Equal(new DiscoveryReward(0, 15, 100), DiscoveryRewardCalculator.Max(broken, 25, 650));
        var roll = DiscoveryRewardCalculator.Roll(broken, 25, 650, new Random(1));
        Assert.Equal(0, roll.Coins);
        Assert.InRange(roll.Gems, 5, 15);
    }

    [Fact]
    public void Merge_OverrideFieldsWinAndNullsInherit()
    {
        var merged = DiscoveryRewardCalculator.Merge(StructureRule(), new DomainDiscoveryOverride
        {
            DomainId = 7, GemsMin = 2, GemsMax = 4, IncludeAncestors = false
        });

        Assert.Equal(("Structure", true, 0.05m, 0.25m, 0.05m, 0.25m, 2, 4, false),
            (merged.DomainType, merged.IsEnabled, merged.ExpUnitsMin, merged.ExpUnitsMax, merged.CoinSalaryHoursMin,
             merged.CoinSalaryHoursMax, merged.GemsMin, merged.GemsMax, merged.IncludeAncestors));
    }

    [Fact]
    public void Merge_WithoutOverrideCopiesTheRule()
    {
        var rule = TownRule();
        var merged = DiscoveryRewardCalculator.Merge(rule, null);

        Assert.NotSame(rule, merged);
        Assert.Equal((rule.IsEnabled, rule.ExpUnitsMax, rule.GemsMax), (merged.IsEnabled, merged.ExpUnitsMax, merged.GemsMax));
    }

    [Fact]
    public void Merge_OverrideCanDisableOneDomainOfAnEnabledType()
    {
        Assert.False(DiscoveryRewardCalculator.Merge(TownRule(), new DomainDiscoveryOverride { IsEnabled = false }).IsEnabled);
        Assert.True(DiscoveryRewardCalculator.Merge(new DiscoveryRewardRule { DomainType = "Town", IsEnabled = false },
            new DomainDiscoveryOverride { IsEnabled = true }).IsEnabled);
    }

    [Theory]
    [InlineData(100, 1.2, 120)]
    [InlineData(5, 1.5, 8)]     // 7.5 rounds away from zero
    [InlineData(3, 1.1, 3)]     // 3.3
    [InlineData(100, 0, 0)]
    [InlineData(100, -2, 0)]    // negative multiplier (direct DB edit) pays nothing
    public void Scale_RoundsAwayFromZeroAndClampsAtZero(int amount, double multiplier, int expected)
    {
        Assert.Equal(expected, CurrencyMultipliersDto.Scale(amount, (decimal)multiplier));
    }

    [Fact]
    public void ToWhole_SaturatesInsteadOfOverflowing()
    {
        Assert.Equal(int.MaxValue, DiscoveryRewardCalculator.ToWhole(1e12m));
        Assert.Equal(0, DiscoveryRewardCalculator.ToWhole(-5m));
    }

    [Fact]
    public void CurrencyMultipliers_UseEachCurrencysOwnPersonalAndRankMultipliers()
    {
        var user = new User { Id = 1, Username = "a", PersonalSalaryMultiplier = 2m, PersonalGemBonusMultiplier = 1m, PersonalExpBonusMultiplier = 3m };
        var ranks = new RankMultipliersDto
        {
            Ranks = new List<ActiveRankDto>
            {
                new() { PermissionGroupId = 20, Name = "Royal", IsPremiumTier = true, SalaryMultiplier = 1.2m, GemBonusMultiplier = 2m, ExpBonusMultiplier = 1m },
                new() { PermissionGroupId = 4, Name = "Default", SalaryMultiplier = 1m, GemBonusMultiplier = 1m, ExpBonusMultiplier = 1.5m }
            }
        };

        var m = CurrencyMultipliersDto.For(user, ranks);

        Assert.Equal((2.4m, 2m, 4.5m), (m.Coins, m.Gems, m.Exp));
        Assert.Equal(new[] { ("personal", 2m), ("rank", 1.2m), ("rank", 1m) }, m.CoinBreakdown.Select(x => (x.Source, x.Value)));
        Assert.Equal(new[] { ("personal", 1m), ("rank", 2m), ("rank", 1m) }, m.GemBreakdown.Select(x => (x.Source, x.Value)));
        Assert.Equal(new[] { ("personal", 3m), ("rank", 1m), ("rank", 1.5m) }, m.ExpBreakdown.Select(x => (x.Source, x.Value)));
        Assert.Equal("Royal", m.CoinBreakdown[1].Name);
    }

    [Fact]
    public void CurrencyMultipliers_WithoutRanksAreJustPersonal()
    {
        var m = CurrencyMultipliersDto.For(new User { Id = 1, Username = "a" }, null);

        Assert.Equal((1m, 1m, 1m), (m.Coins, m.Gems, m.Exp));
        Assert.Single(m.CoinBreakdown);
    }
}
