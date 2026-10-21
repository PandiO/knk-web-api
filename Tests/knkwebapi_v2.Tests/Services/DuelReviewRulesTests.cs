using Xunit;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Duel;

namespace knkwebapi_v2.Tests.Services;

/// <summary>Signals S1–S6 and the tiers (arena duels DESIGN.md §10.3, KNG-213), without a database.</summary>
public class DuelReviewRulesTests
{
    private const int Winner = 1, Loser = 2;

    private static DuelTelemetryDto Fight(double seconds = 60, double winnerDamage = 40, double loserDamage = 20, int loserAttempts = 10,
        int? loserArmor = 10, bool? loserWeapon = true) => new()
    {
        DurationSeconds = seconds,
        Challenger = new DuelSideTelemetryDto { DamageDealt = winnerDamage, AttackAttempts = 12, ArmorPoints = 10, HadWeapon = true },
        Opponent = new DuelSideTelemetryDto { DamageDealt = loserDamage, AttackAttempts = loserAttempts, ArmorPoints = loserArmor, HadWeapon = loserWeapon }
    };

    private static DuelReviewOutcome Evaluate(DuelTelemetryDto? telemetry, DuelPolicy? policy = null, DuelGearMode mode = DuelGearMode.OWN_GEAR_KEEP,
        int[]? priorWinners = null, int youngOpponents = 0, int pairFlags = 0, int beneficiaryFlags = 0) =>
        DuelReviewRules.Evaluate(new DuelReviewInput
        {
            Policy = policy ?? new DuelPolicy(),
            GearMode = mode,
            Telemetry = telemetry,
            WinnerIsChallenger = true,
            WinnerUserId = Winner,
            PriorPairWinners = priorWinners ?? Array.Empty<int>(),
            YoungOpponents = youngOpponents,
            PriorPairFlags = pairFlags,
            PriorBeneficiaryFlags = beneficiaryFlags
        });

    [Fact]
    public void ACleanFight_IsTierZero()
    {
        var outcome = Evaluate(Fight());
        Assert.Equal((0, 0), (outcome.Tier, outcome.Signals.Count));
    }

    [Theory]
    [InlineData(60, 40, 3.9, 10, 10, true, DuelReviewRules.S1)]   // loser dealt < 10% of the winner's damage
    [InlineData(9.9, 40, 20, 10, 10, true, DuelReviewRules.S2)]   // under 10 s
    [InlineData(60, 40, 20, 10, 0, false, DuelReviewRules.S3)]    // no weapon, no armour
    [InlineData(60, 40, 20, 0, 10, true, DuelReviewRules.S4)]     // never swung
    public void EachTelemetrySignal_FiresAlone(double seconds, double winnerDamage, double loserDamage, int attempts, int armor, bool weapon, string signal)
    {
        var outcome = Evaluate(Fight(seconds, winnerDamage, loserDamage, attempts, armor, weapon));
        Assert.Equal(new[] { signal }, outcome.Signals);
        Assert.Equal(1, outcome.Tier);
    }

    [Fact]
    public void S1AtExactlyTenPercent_AndS2AtExactlyTenSeconds_DontFire()
    {
        Assert.Empty(Evaluate(Fight(seconds: 10, winnerDamage: 40, loserDamage: 4)).Signals);
    }

    [Fact]
    public void S3_OnlyInOwnGearModes_AndOnlyWhenBothAreKnown()
    {
        var unarmed = Fight(loserArmor: 0, loserWeapon: false);
        Assert.Empty(Evaluate(unarmed, mode: DuelGearMode.FIXED_KIT).Signals);
        Assert.Contains(DuelReviewRules.S3, Evaluate(unarmed, mode: DuelGearMode.OWN_GEAR_DROP).Signals);
        Assert.Empty(Evaluate(Fight(loserArmor: null, loserWeapon: false)).Signals);
    }

    [Fact]
    public void S5_NeedsTheSameWinnerInEveryOneOfTheLastThree()
    {
        Assert.Equal(new[] { DuelReviewRules.S5 }, Evaluate(Fight(), priorWinners: new[] { Winner, Winner, Loser }).Signals);
        Assert.Empty(Evaluate(Fight(), priorWinners: new[] { Winner, Loser }).Signals);
        Assert.Empty(Evaluate(Fight(), priorWinners: new[] { Winner }).Signals);
    }

    [Fact]
    public void S6_AtThreeYoungOpponents()
    {
        Assert.Empty(Evaluate(Fight(), youngOpponents: 2).Signals);
        Assert.Equal(new[] { DuelReviewRules.S6 }, Evaluate(Fight(), youngOpponents: 3).Signals);
    }

    [Fact]
    public void WithoutTelemetry_OnlyTheHistorySignalsCanFire()
    {
        Assert.Empty(Evaluate(null).Signals);
        Assert.Equal(new[] { DuelReviewRules.S6 }, Evaluate(null, youngOpponents: 3).Signals);
    }

    [Fact]
    public void DisabledSignals_DontFire()
    {
        var policy = new DuelPolicy { S1Enabled = false, S2Enabled = false, S3Enabled = false, S4Enabled = false, S5Enabled = false, S6Enabled = false };
        var outcome = Evaluate(Fight(seconds: 1, loserDamage: 0, loserAttempts: 0, loserArmor: 0, loserWeapon: false), policy,
            priorWinners: new[] { Winner, Winner }, youngOpponents: 9);
        Assert.Equal(0, outcome.Tier);
    }

    [Fact]
    public void ASecondCountingFlag_ByPairOrByBeneficiary_Holds()
    {
        var thrown = Fight(loserAttempts: 0);
        var byPair = Evaluate(thrown, pairFlags: 1);
        var byBeneficiary = Evaluate(thrown, beneficiaryFlags: 1);

        Assert.Equal((2, true, false), (byPair.Tier, byPair.EscalatedByPair, byPair.EscalatedByBeneficiary));
        Assert.Equal((2, false, true), (byBeneficiary.Tier, byBeneficiary.EscalatedByPair, byBeneficiary.EscalatedByBeneficiary));
        Assert.Equal(0, Evaluate(Fight(), pairFlags: 5).Tier); // earlier flags alone never flag a clean fight
    }

    [Fact]
    public void AFirstFlagNeverHolds_EvenIfThePolicySaysOne()
    {
        var policy = new DuelPolicy { EscalationFlagCount = 1 };
        var outcome = Evaluate(Fight(seconds: 1, loserDamage: 0, loserAttempts: 0, loserArmor: 0, loserWeapon: false), policy,
            priorWinners: new[] { Winner, Winner }, youngOpponents: 9);

        Assert.Equal(6, outcome.Signals.Count);
        Assert.Equal(1, outcome.Tier);
    }

    [Fact]
    public void AHigherFlagCount_NeedsMoreEarlierFlags()
    {
        var policy = new DuelPolicy { EscalationFlagCount = 3 };
        var thrown = Fight(loserAttempts: 0);
        Assert.Equal(1, Evaluate(thrown, policy, pairFlags: 1).Tier);
        Assert.Equal(2, Evaluate(thrown, policy, pairFlags: 2).Tier);
    }
}
