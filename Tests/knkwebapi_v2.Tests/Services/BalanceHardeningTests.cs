using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-22 (currency-payments Phase 0, DESIGN.md §1.4 A1/A2/A8): caps and checked arithmetic,
/// the generic user edit no longer writing balances, and column-scoped user writes.
/// <para>
/// Gap: every test here runs on EF InMemory, which has no transactions and ignores
/// SELECT … FOR UPDATE, so the row lock (UserRepository.RunWithUsersLockedAsync) is only
/// checked for being called, not for actually serialising two requests. That needs a real
/// MySQL (the planned requires-mysql test fixture, currency Phase 1).
/// </para>
/// </summary>
public class BalanceHardeningTests
{
    // ===== BalanceLimits =====

    [Fact]
    public void Caps_AreTheAgreedValues()
    {
        Assert.Equal(999_999_999, BalanceLimits.MaxCoins);
        Assert.Equal(999_999, BalanceLimits.MaxGems);
    }

    [Fact]
    public void ApplyCoins_UpToTheCap_IsFine_AndOneMoreIsRejected()
    {
        Assert.Equal(BalanceLimits.MaxCoins, BalanceLimits.ApplyCoins(BalanceLimits.MaxCoins - 10, 10));
        var ex = Assert.Throws<BalanceCapExceededException>(() => BalanceLimits.ApplyCoins(BalanceLimits.MaxCoins - 10, 11));
        Assert.StartsWith("BalanceCapExceeded", ex.Message);
    }

    [Fact]
    public void ApplyGems_AboveTheCap_IsRejected()
    {
        Assert.Throws<BalanceCapExceededException>(() => BalanceLimits.ApplyGems(0, BalanceLimits.MaxGems + 1L));
    }

    [Fact]
    public void ApplyCoins_BelowZero_IsInsufficient()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BalanceLimits.ApplyCoins(5, -6));
        Assert.IsNotType<BalanceCapExceededException>(ex);
        Assert.StartsWith("Insufficient coins", ex.Message);
    }

    [Fact]
    public void ApplyExperience_IntOverflow_IsACapErrorNotAWrapAround()
    {
        // Unchecked int math wrapped this to a negative number (A8).
        Assert.Throws<BalanceCapExceededException>(() => BalanceLimits.ApplyExperience(int.MaxValue, int.MaxValue));
    }

    [Fact]
    public void ToWholeAmount_HugeOrNegative()
    {
        Assert.Equal(0, BalanceLimits.ToWholeAmount(() => -5.5m, "salary"));
        Assert.Equal(3, BalanceLimits.ToWholeAmount(() => 2.5m, "salary"));
        var huge = decimal.MaxValue;
        Assert.Throws<BalanceCapExceededException>(() => BalanceLimits.ToWholeAmount(() => huge * 2, "salary"));
        Assert.Throws<BalanceCapExceededException>(() => BalanceLimits.ToWholeAmount(() => 1e15m, "salary"));
    }

    // ===== UserService with mocks =====

    private readonly Mock<IUserRepository> _repo = new();
    private readonly Mock<ITitleService> _titleService = new();
    private readonly Mock<IUserPermissionGroupService> _membershipService = new();
    private readonly Mock<IAuditLogService> _auditLog = new();

    private UserService Service(IUserRepository repo, IMapper mapper) => new(
        repo,
        mapper,
        new Mock<IPasswordService>().Object,
        new Mock<ILinkCodeService>().Object,
        _titleService.Object,
        _membershipService.Object,
        _auditLog.Object,
        new Mock<IPermissionGroupRepository>().Object,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<UserService>.Instance);

    private UserService MockedService()
    {
        _repo.Setup(r => r.RunWithUsersLockedAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<Func<Task>>()))
            .Returns((IEnumerable<int> _, Func<Task> work) => work());
        _titleService.Setup(s => s.ResolveAsync(It.IsAny<int>(), It.IsAny<Gender?>())).ReturnsAsync(new TitleResolutionDto());
        return Service(_repo.Object, new Mock<IMapper>().Object);
    }

    [Fact]
    public async Task AdjustBalances_AboveTheCoinCap_ChangesNothing()
    {
        var user = new User { Id = 1, Username = "rich", Coins = BalanceLimits.MaxCoins - 1 };
        _repo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);

        await Assert.ThrowsAsync<BalanceCapExceededException>(() =>
            MockedService().AdjustBalancesAsync(1, coinsDelta: 2, gemsDelta: 0, experienceDelta: 0, reason: "test"));

        Assert.Equal(BalanceLimits.MaxCoins - 1, user.Coins);
        _repo.Verify(r => r.SaveBalancesAsync(It.IsAny<User>()), Times.Never);
        _auditLog.Verify(a => a.RecordAsync(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<AuditAction>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task AdjustBalances_AboveTheGemCap_IsRejected()
    {
        _repo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "gemmy", Gems = 10 });

        await Assert.ThrowsAsync<BalanceCapExceededException>(() =>
            MockedService().AdjustBalancesAsync(1, coinsDelta: 0, gemsDelta: BalanceLimits.MaxGems, experienceDelta: 0, reason: "test"));
    }

    [Fact]
    public async Task AdjustBalances_RunsUnderTheUsersRowLockAndSavesBalances()
    {
        var user = new User { Id = 1, Username = "p", Coins = 100 };
        _repo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);

        var result = await MockedService().AdjustBalancesAsync(1, coinsDelta: 50, gemsDelta: 0, experienceDelta: 0, reason: "test");

        Assert.Equal(150, result.NewCoins);
        _repo.Verify(r => r.RunWithUsersLockedAsync(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 1 })), It.IsAny<Func<Task>>()), Times.Once);
        _repo.Verify(r => r.SaveBalancesAsync(user), Times.Once);
        _repo.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task AdjustBalances_PromotionBonusPastTheCap_ChangesNothing()
    {
        var user = new User { Id = 1, Username = "almost", Coins = BalanceLimits.MaxCoins - 5, ExperiencePoints = 0 };
        _repo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);
        _titleService.Setup(s => s.GetAllOrderedAsync()).ReturnsAsync(new List<TitleBracket>
        {
            new() { Id = 1, MaleName = "A", FemaleName = "A", MinExperience = 0 },
            new() { Id = 2, MaleName = "B", FemaleName = "B", MinExperience = 100, CoinBonus = 10 }
        });
        _membershipService.Setup(m => m.GetActiveRankMultipliersAsync(1)).ReturnsAsync(RankMultipliersDto.Neutral);
        var service = MockedService();

        await Assert.ThrowsAsync<BalanceCapExceededException>(() =>
            service.AdjustBalancesAsync(1, coinsDelta: 0, gemsDelta: 0, experienceDelta: 100, reason: "test"));

        Assert.Equal(0, user.ExperiencePoints);
        Assert.Equal(BalanceLimits.MaxCoins - 5, user.Coins);
    }

    [Fact]
    public async Task AdjustBalances_AbsurdMultiplier_IsACapErrorNotACrash()
    {
        var user = new User { Id = 1, Username = "m", ExperiencePoints = 0, PersonalSalaryMultiplier = 1e20m };
        _repo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);
        _titleService.Setup(s => s.GetAllOrderedAsync()).ReturnsAsync(new List<TitleBracket>
        {
            new() { Id = 1, MaleName = "A", FemaleName = "A", MinExperience = 0 },
            new() { Id = 2, MaleName = "B", FemaleName = "B", MinExperience = 100, CoinBonus = 1_000_000 }
        });
        _membershipService.Setup(m => m.GetActiveRankMultipliersAsync(1)).ReturnsAsync(RankMultipliersDto.Neutral);

        await Assert.ThrowsAsync<BalanceCapExceededException>(() =>
            MockedService().AdjustBalancesAsync(1, coinsDelta: 0, gemsDelta: 0, experienceDelta: 100, reason: "test"));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(100.01)]
    public async Task SetPersonalMultipliers_OutOfRange_IsRejected(double value)
    {
        _repo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new User { Id = 1, Username = "p" });

        await Assert.ThrowsAsync<ArgumentException>(() => MockedService().SetPersonalMultipliersAsync(1,
            new UpdatePersonalMultipliersDto { PersonalGemBonusMultiplier = (decimal)value }));
        _repo.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task SetPersonalMultipliers_ChangesOnlyWhatIsSent_AndIsAudited()
    {
        var user = new User { Id = 1, Username = "p", PersonalSalaryMultiplier = 1m, PersonalGemBonusMultiplier = 1m, PersonalExpBonusMultiplier = 1m };
        _repo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);

        await MockedService().SetPersonalMultipliersAsync(1, new UpdatePersonalMultipliersDto { PersonalSalaryMultiplier = 2.5m }, actorUserId: 9);

        Assert.Equal(2.5m, user.PersonalSalaryMultiplier);
        Assert.Equal(1m, user.PersonalGemBonusMultiplier);
        _repo.Verify(r => r.UpdateUserAsync(user), Times.Once);
        _auditLog.Verify(a => a.RecordAsync(9, 1, AuditAction.BalanceAdjusted, It.Is<string?>(d => d!.Contains("PersonalMultipliers"))), Times.Once);
    }

    // ===== Real mapper + EF InMemory =====

    private static readonly string DbName = $"balance-hardening-{Guid.NewGuid()}";

    private static KnKDbContext NewContext(string name) =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(name).Options);

    private static IMapper RealMapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<knkwebapi_v2.Mapping.UserMappingProfile>()).CreateMapper();

    private static async Task<int> SeedUserAsync(string dbName)
    {
        await using var ctx = NewContext(dbName);
        var user = new User
        {
            Username = "victim", Email = "v@example.com", Coins = 250, Gems = 50, ExperiencePoints = 10,
            PersonalSalaryMultiplier = 1m, PersonalGemBonusMultiplier = 1m, PersonalExpBonusMultiplier = 1m
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task GenericUserEdit_IgnoresBalancesXpAndMultipliers()
    {
        var dbName = $"{DbName}-generic";
        var id = await SeedUserAsync(dbName);

        await using (var ctx = NewContext(dbName))
        {
            var service = Service(new UserRepository(ctx), RealMapper());
            await service.UpdateAsync(id, new UserDto
            {
                Id = id, Username = "victim-renamed", Email = "v@example.com", CreatedAt = DateTime.UtcNow.ToString("O"),
                Coins = 999_999_999, Gems = 999_999, ExperiencePoints = 5_000_000,
                PersonalSalaryMultiplier = 50m, PersonalGemBonusMultiplier = 50m, PersonalExpBonusMultiplier = 50m
            });
        }

        await using var check = NewContext(dbName);
        var stored = await check.Users.SingleAsync(u => u.Id == id);
        Assert.Equal("victim-renamed", stored.Username);
        Assert.Equal(250, stored.Coins);
        Assert.Equal(50, stored.Gems);
        Assert.Equal(10, stored.ExperiencePoints);
        Assert.Equal(1m, stored.PersonalSalaryMultiplier);
        Assert.Equal(1m, stored.PersonalGemBonusMultiplier);
        Assert.Equal(1m, stored.PersonalExpBonusMultiplier);
    }

    [Fact]
    public async Task UnrelatedWritesFromAnOlderCopy_DoNotPutAnOldBalanceBack()
    {
        // A2: request A loads the user, request B pays out coins, then A saves presence / a
        // freeze / a profile edit. A's copy still holds the old coins; before, A's full-row
        // Update() wrote them back and B's payout vanished.
        var dbName = $"{DbName}-stale";
        var id = await SeedUserAsync(dbName);

        await using var requestA = NewContext(dbName);
        var repoA = new UserRepository(requestA);
        var staleCopy = await repoA.GetByIdAsync(id);
        Assert.Equal(250, staleCopy!.Coins);

        await using (var requestB = NewContext(dbName))
        {
            var repoB = new UserRepository(requestB);
            await repoB.RunWithUsersLockedAsync(new[] { id }, async () =>
            {
                var fresh = await repoB.GetByIdAsync(id);
                fresh!.Coins += 1000;
                await repoB.SaveBalancesAsync(fresh);
            });
        }

        await repoA.UpdatePresenceAsync(id, true);
        staleCopy.IsFrozen = true;
        staleCopy.FrozenReason = "test";
        await repoA.UpdateUserAsync(staleCopy);

        await using var check = NewContext(dbName);
        var stored = await check.Users.SingleAsync(u => u.Id == id);
        Assert.Equal(1250, stored.Coins);
        Assert.True(stored.IsOnline);
        Assert.True(stored.IsFrozen);
    }

    [Fact]
    public async Task UpdateUserAsync_NeverWritesBalanceColumns()
    {
        var dbName = $"{DbName}-guard";
        var id = await SeedUserAsync(dbName);

        await using (var ctx = NewContext(dbName))
        {
            var repo = new UserRepository(ctx);
            var user = await repo.GetByIdAsync(id);
            user!.Coins = 5;
            user.Gems = 5;
            user.Username = "renamed";
            await repo.UpdateUserAsync(user);
        }

        await using var check = NewContext(dbName);
        var stored = await check.Users.SingleAsync(u => u.Id == id);
        Assert.Equal("renamed", stored.Username);
        Assert.Equal(250, stored.Coins);
        Assert.Equal(50, stored.Gems);
    }

    [Fact]
    public async Task RunWithUsersLocked_FailedWork_DiscardsUnsavedChanges()
    {
        var dbName = $"{DbName}-rollback";
        var id = await SeedUserAsync(dbName);

        await using var ctx = NewContext(dbName);
        var repo = new UserRepository(ctx);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.RunWithUsersLockedAsync(new[] { id }, async () =>
        {
            var user = await repo.GetByIdAsync(id);
            user!.Coins = 999;
            throw new InvalidOperationException("boom");
        }));

        // A later, unrelated save in the same request must not carry the failed change along.
        await repo.UpdatePresenceAsync(id, true);
        var tracked = await repo.GetByIdAsync(id);
        Assert.Equal(250, tracked!.Coins);

        await using var check = NewContext(dbName);
        Assert.Equal(250, (await check.Users.SingleAsync(u => u.Id == id)).Coins);
    }

    // ===== Salary =====

    [Fact]
    public async Task Salary_AtTheCoinCap_IsRejectedAndTheClockDoesNotMove()
    {
        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.RunWithUsersLockedAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<Func<Task>>()))
            .Returns((IEnumerable<int> _, Func<Task> work) => work());
        var lastPayout = DateTime.UtcNow.AddHours(-2);
        var user = new User { Id = 1, Username = "rich", Coins = BalanceLimits.MaxCoins, LastSalaryPayoutAt = lastPayout, PersonalSalaryMultiplier = 1m };
        repo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);
        var memberships = new Mock<IUserPermissionGroupRepository>();
        memberships.Setup(m => m.GetByUserAsync(1)).ReturnsAsync(new List<UserPermissionGroup>());
        var config = new Mock<ISalaryConfigurationService>();
        config.Setup(c => c.GetAsync()).ReturnsAsync(new SalaryConfigurationDto { GlobalMultiplier = 1m, OfflinePayoutMaxHours = 720 });
        var titles = new Mock<ITitleService>();
        titles.Setup(t => t.ResolveAsync(It.IsAny<int>(), It.IsAny<Gender?>())).ReturnsAsync(new TitleResolutionDto { Salary = 650 });
        var service = new SalaryService(repo.Object, memberships.Object, config.Object, titles.Object, _auditLog.Object);

        await Assert.ThrowsAsync<BalanceCapExceededException>(() => service.PayOutAsync(1));

        Assert.Equal(BalanceLimits.MaxCoins, user.Coins);
        Assert.Equal(lastPayout, user.LastSalaryPayoutAt);
        repo.Verify(r => r.SaveBalancesAsync(It.IsAny<User>()), Times.Never);
    }
}
