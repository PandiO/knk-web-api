using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Siege Phase 6 (docs/specs/siege-minigame/IMPLEMENTATION_PLAN.md Phase 6): the match lifecycle
/// (create, start, left, complete, abort, startup recovery, history) against the real repository on
/// an InMemory database - rewards granted once (idempotent double-complete), abort grants nothing,
/// XP moves the player's TitleBracket.
///
/// Scenario 100 (SiegeTestData): Defender 201 (alliance 1) and Attacker 202 (alliance 2);
/// objectives 501 (IV) and 502, both held by the Defender at the start. Default rewards: win
/// 100 coins / 10 XP / 1 gem, holding 50 / 5, capture 50 / 5. Title brackets: Recruit (0 XP),
/// Squire (30 XP, +7 coins, +1 gem, +2 XP bonus), Knight (1000 XP).
/// </summary>
public class SiegeMatchServiceTests : IAsyncLifetime
{
    private KnKDbContext _context = null!;
    private SiegeMatchService _service = null!;
    private Mock<IPlayerNotificationQueue> _notifications = null!;

    public async Task InitializeAsync()
    {
        _context = SiegeTestData.NewContext("SiegeMatch");
        await SiegeTestData.SeedValidScenarioAsync(_context);

        _context.SiegeLobbies.Add(new SiegeLobby
        {
            Id = 1, Name = "Siege - Cinix", Key = "cinix", IsEnabled = true,
            Rotation = { new SiegeLobbyScenario { SiegeScenarioId = 100 } }
        });
        _context.TitleBrackets.Add(new TitleBracket { Id = 1, MaleName = "Recruit", FemaleName = "Recruit", MinExperience = 0 });
        _context.TitleBrackets.Add(new TitleBracket { Id = 2, MaleName = "Squire", FemaleName = "Squire", MinExperience = 30, CoinBonus = 7, GemBonus = 1, ExpBonus = 2 });
        _context.TitleBrackets.Add(new TitleBracket { Id = 3, MaleName = "Knight", FemaleName = "Dame", MinExperience = 1000 });
        for (var id = 1; id <= 6; id++)
        {
            _context.Users.Add(new User { Id = id, Username = $"player{id}", Uuid = $"uuid-{id}", Coins = 10, Gems = 0, ExperiencePoints = 0 });
        }
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        _notifications = new Mock<IPlayerNotificationQueue>();
        _service = new SiegeMatchService(
            new SiegeMatchRepository(_context),
            new TitleService(new TitleBracketRepository(_context)),
            _notifications.Object);
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    // ---- helpers ----

    private async Task<int> StartedMatchAsync(params (int UserId, int TeamId)[] participants)
    {
        var created = await _service.CreateAsync(new SiegeMatchCreateDto { SiegeLobbyId = 1, SiegeScenarioId = 100 });
        await _service.StartAsync(created.Id, new SiegeMatchStartDto
        {
            Participants = participants.Select(p => new SiegeMatchParticipantStartDto { UserId = p.UserId, SiegeTeamId = p.TeamId }).ToList()
        });
        _context.ChangeTracker.Clear();
        return created.Id;
    }

    private static SiegeMatchParticipantResultDto Result(int userId, int teamId, int kills = 0, int captures = 0) =>
        new() { UserId = userId, SiegeTeamId = teamId, Kills = kills, Captures = captures };

    // Attackers (users 3, 4) take the Gatehouse (user 3) and then the Keep (user 4): IV win for alliance 2.
    private static SiegeMatchCompleteDto AttackersWin(params SiegeMatchParticipantResultDto[] participants) => new()
    {
        EndReason = SiegeMatchEndReason.InstantVictory,
        WinningAllianceGroup = 2,
        Participants = participants.ToList(),
        Objectives =
        {
            new SiegeMatchObjectiveResultInputDto { SiegeObjectiveId = 502, FinalHolderTeamId = 202, CapturedByUserId = 3, CapturedAt = DateTime.UtcNow },
            new SiegeMatchObjectiveResultInputDto { SiegeObjectiveId = 501, FinalHolderTeamId = 202, CapturedByUserId = 4, CapturedAt = DateTime.UtcNow }
        }
    };

    private async Task<User> UserAsync(int id)
    {
        _context.ChangeTracker.Clear();
        return await _context.Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    // ---- create / start / left ----

    [Fact]
    public async Task Create_WritesACreatedRow()
    {
        var created = await _service.CreateAsync(new SiegeMatchCreateDto { SiegeLobbyId = 1, SiegeScenarioId = 100 });

        Assert.True(created.Id > 0);
        Assert.Equal(SiegeMatchStatus.Created, created.Status);
        Assert.Equal("Siege of Cinix", created.SiegeScenarioName);
        Assert.Null(created.StartedAt);
    }

    [Fact]
    public async Task Create_UnknownLobbyOrScenario_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new SiegeMatchCreateDto { SiegeLobbyId = 99, SiegeScenarioId = 100 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new SiegeMatchCreateDto { SiegeLobbyId = 1, SiegeScenarioId = 999 }));
    }

    [Fact]
    public async Task Start_RecordsParticipants_AndARepeatChangesNothing()
    {
        var id = await StartedMatchAsync((1, 201), (3, 202));

        var again = await _service.StartAsync(id, new SiegeMatchStartDto
        {
            Participants = { new SiegeMatchParticipantStartDto { UserId = 5, SiegeTeamId = 202 } }
        });

        Assert.Equal(SiegeMatchStatus.InProgress, again.Status);
        Assert.NotNull(again.StartedAt);
        Assert.Equal(new[] { 1, 3 }, again.Participants.Select(p => p.UserId).OrderBy(x => x));
        Assert.Equal(201, again.Participants.Single(p => p.UserId == 1).SiegeTeamId);
    }

    [Fact]
    public async Task Start_TeamOfAnotherScenario_OrUnknownUser_IsRejected()
    {
        var created = await _service.CreateAsync(new SiegeMatchCreateDto { SiegeLobbyId = 1, SiegeScenarioId = 100 });

        await Assert.ThrowsAsync<ArgumentException>(() => _service.StartAsync(created.Id, new SiegeMatchStartDto
        {
            Participants = { new SiegeMatchParticipantStartDto { UserId = 1, SiegeTeamId = 999 } }
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.StartAsync(created.Id, new SiegeMatchStartDto
        {
            Participants = { new SiegeMatchParticipantStartDto { UserId = 77, SiegeTeamId = 201 } }
        }));
    }

    [Fact]
    public async Task Left_SetsLeftAtOnce_UnknownParticipantIsNotFound()
    {
        var id = await StartedMatchAsync((1, 201), (3, 202));
        var first = new DateTime(2026, 9, 26, 20, 0, 0, DateTimeKind.Utc);

        await _service.ParticipantLeftAsync(id, 3, new SiegeMatchParticipantLeftDto { LeftAt = first });
        await _service.ParticipantLeftAsync(id, 3, new SiegeMatchParticipantLeftDto { LeftAt = first.AddMinutes(5) });
        _context.ChangeTracker.Clear();

        var match = await _service.GetByIdAsync(id);
        Assert.Equal(first, match!.Participants.Single(p => p.UserId == 3).LeftAt);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.ParticipantLeftAsync(id, 5, null));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.ParticipantLeftAsync(999, 1, null));
    }

    // ---- complete ----

    [Fact]
    public async Task Complete_GrantsRewardsPerDesign_AndStoresThemOnTheParticipants()
    {
        var id = await StartedMatchAsync((1, 201), (2, 201), (3, 202), (4, 202), (5, 202));
        await _service.ParticipantLeftAsync(id, 5, null); // left early: nothing
        _context.ChangeTracker.Clear();

        var result = await _service.CompleteAsync(id, AttackersWin(
            Result(1, 201), Result(2, 201), Result(3, 202, kills: 4, captures: 1), Result(4, 202, captures: 1)));

        Assert.False(result.AlreadyCompleted);
        Assert.Equal(SiegeMatchStatus.Completed, result.Status);
        var r3 = result.Rewards.Single(r => r.UserId == 3);
        Assert.Equal((true, 2, 1), (r3.Won, r3.HoldingCount, r3.CaptureCount));
        Assert.Equal((250, 25, 1), (r3.Coins, r3.Experience, r3.Gems));
        Assert.Equal((0, 0, 0), (result.Rewards.Single(r => r.UserId == 1).Coins, result.Rewards.Single(r => r.UserId == 1).Experience, result.Rewards.Single(r => r.UserId == 1).Gems));
        var r5 = result.Rewards.Single(r => r.UserId == 5);
        Assert.False(r5.PresentAtEnd);
        Assert.Equal(0, r5.Coins);

        // Balances: coins/gems/XP added (25 XP stays below Squire's 30, so no bracket bonus here).
        var u3 = await UserAsync(3);
        Assert.Equal((10 + 250, 1, 25), (u3.Coins, u3.Gems, u3.ExperiencePoints));
        var u1 = await UserAsync(1);
        Assert.Equal((10, 0, 0), (u1.Coins, u1.Gems, u1.ExperiencePoints));
        var u5 = await UserAsync(5);
        Assert.Equal((10, 0, 0), (u5.Coins, u5.Gems, u5.ExperiencePoints));

        var stored = await _service.GetByIdAsync(id);
        Assert.Equal(SiegeMatchEndReason.InstantVictory, stored!.EndReason);
        Assert.Equal(2, stored.WinningAllianceGroup);
        Assert.NotNull(stored.EndedAt);
        var p3 = stored.Participants.Single(p => p.UserId == 3);
        Assert.Equal((250, 25, 1, 4, 1), (p3.CoinsAwarded, p3.ExpAwarded, p3.GemsAwarded, p3.Kills, p3.Captures));
        Assert.Equal(2, stored.ObjectiveResults.Count);
    }

    [Fact]
    public async Task Complete_CoinRewardsGetThePersonalAndActiveRankMultipliers_XpAndGemsDoNot()
    {
        // Smoke test 2026-09-26: coins x PersonalSalaryMultiplier x active groups' SalaryMultiplier.
        var premium = new PermissionGroup { Id = 20, Name = "Royal", SalaryMultiplier = 1.5m, IsPremiumTier = true };
        var expired = new PermissionGroup { Id = 21, Name = "Old", SalaryMultiplier = 3.0m };
        _context.PermissionGroups.AddRange(premium, expired);
        _context.UserPermissionGroups.Add(new UserPermissionGroup { UserId = 3, PermissionGroupId = 20 });
        _context.UserPermissionGroups.Add(new UserPermissionGroup { UserId = 3, PermissionGroupId = 21, ExpiresAt = DateTime.UtcNow.AddDays(-1) });
        var u = await _context.Users.SingleAsync(x => x.Id == 3);
        u.PersonalSalaryMultiplier = 2.0m;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        var service = new SiegeMatchService(new SiegeMatchRepository(_context),
            new TitleService(new TitleBracketRepository(_context)), _notifications.Object, null,
            new UserPermissionGroupRepository(_context));

        var created = await service.CreateAsync(new SiegeMatchCreateDto { SiegeLobbyId = 1, SiegeScenarioId = 100 });
        await service.StartAsync(created.Id, new SiegeMatchStartDto
        {
            Participants = { new SiegeMatchParticipantStartDto { UserId = 3, SiegeTeamId = 202 },
                             new SiegeMatchParticipantStartDto { UserId = 4, SiegeTeamId = 202 },
                             new SiegeMatchParticipantStartDto { UserId = 1, SiegeTeamId = 201 } }
        });
        _context.ChangeTracker.Clear();

        var dto = AttackersWin(Result(3, 202, captures: 1), Result(4, 202, captures: 1), Result(1, 201));
        var result = await service.CompleteAsync(created.Id, dto);

        var r3 = result.Rewards.Single(r => r.UserId == 3);
        Assert.Equal((250, 3.0m, 750, 25, 1), (r3.BaseCoins, r3.CoinMultiplier, r3.Coins, r3.Experience, r3.Gems));
        var r4 = result.Rewards.Single(r => r.UserId == 4); // no groups, personal 1.0
        Assert.Equal((250, 1.0m, 250), (r4.BaseCoins, r4.CoinMultiplier, r4.Coins));
        var u3 = await UserAsync(3);
        Assert.Equal((10 + 750, 1, 25), (u3.Coins, u3.Gems, u3.ExperiencePoints));

        _context.ChangeTracker.Clear();
        var again = (await service.CompleteAsync(created.Id, dto)).Rewards.Single(r => r.UserId == 3);
        Assert.Equal((250, 3.0m, 750), (again.BaseCoins, again.CoinMultiplier, again.Coins));
        Assert.Equal(10 + 750, (await UserAsync(3)).Coins);
    }

    [Fact]
    public async Task Complete_Twice_ReturnsTheStoredResult_AndGrantsNothingMore()
    {
        var id = await StartedMatchAsync((3, 202), (4, 202), (1, 201));
        var dto = AttackersWin(Result(3, 202), Result(4, 202), Result(1, 201));

        var first = await _service.CompleteAsync(id, dto);
        _context.ChangeTracker.Clear();
        var second = await _service.CompleteAsync(id, dto);

        Assert.False(first.AlreadyCompleted);
        Assert.True(second.AlreadyCompleted);
        foreach (var r in first.Rewards)
        {
            var again = second.Rewards.Single(x => x.UserId == r.UserId);
            Assert.Equal((r.Won, r.HoldingCount, r.CaptureCount, r.Coins, r.Experience, r.Gems),
                (again.Won, again.HoldingCount, again.CaptureCount, again.Coins, again.Experience, again.Gems));
        }

        var u3 = await UserAsync(3);
        Assert.Equal((260, 1, 25), (u3.Coins, u3.Gems, u3.ExperiencePoints));
        Assert.Equal(2, (await _service.GetByIdAsync(id))!.ObjectiveResults.Count); // not appended twice
    }

    [Fact]
    public async Task Complete_XpMovesTheTitleBracket_GrantsItsBonusOnce_AndNotifiesThePlugin()
    {
        var user = await _context.Users.SingleAsync(u => u.Id == 3);
        user.ExperiencePoints = 20; // Recruit; +25 -> 45 = Squire (MinExperience 30)
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        var id = await StartedMatchAsync((3, 202), (1, 201));

        var result = await _service.CompleteAsync(id, AttackersWin(Result(3, 202), Result(1, 201)));

        var reward = result.Rewards.Single(r => r.UserId == 3);
        Assert.NotNull(reward.TitleChange);
        Assert.Equal("promotion", reward.TitleChange!.Direction);
        Assert.Equal((1, 2), (reward.TitleChange.FromTitleBracketId, reward.TitleChange.ToTitleBracketId));
        Assert.Equal(7, reward.TitleChange.CoinBonusGranted);

        var u3 = await UserAsync(3);
        Assert.Equal(20 + 25 + 2, u3.ExperiencePoints);           // + Squire's ExpBonus
        Assert.Equal(10 + 250 + 7, u3.Coins);                     // + Squire's CoinBonus
        Assert.Equal(1 + 1, u3.Gems);                             // win gem + Squire's GemBonus
        var resolved = await new TitleService(new TitleBracketRepository(_context)).ResolveAsync(u3.ExperiencePoints);
        Assert.Equal(2, resolved.TitleBracketId);

        // The siege amounts stay the siege's; the bracket bonus is reported separately.
        var p3 = (await _service.GetByIdAsync(id))!.Participants.Single(p => p.UserId == 3);
        Assert.Equal((250, 25, 1), (p3.CoinsAwarded, p3.ExpAwarded, p3.GemsAwarded));

        _notifications.Verify(q => q.Enqueue(3, "uuid-3", "player3", PlayerNotificationTypes.TitleChanged, It.IsAny<TitleChangeResultDto?>()), Times.Once);
        _notifications.Verify(q => q.Enqueue(1, It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TitleChangeResultDto?>()), Times.Never);

        // A repeat call neither re-grants nor re-notifies.
        _context.ChangeTracker.Clear();
        var again = await _service.CompleteAsync(id, AttackersWin(Result(3, 202), Result(1, 201)));
        Assert.Null(again.Rewards.Single(r => r.UserId == 3).TitleChange);
        _notifications.Verify(q => q.Enqueue(3, It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TitleChangeResultDto?>()), Times.Once);
        Assert.Equal(47, (await UserAsync(3)).ExperiencePoints);
    }

    [Fact]
    public async Task Complete_ParticipantMissingFromTheReport_IsClosedAndGetsNothing()
    {
        var id = await StartedMatchAsync((3, 202), (4, 202), (1, 201));

        var result = await _service.CompleteAsync(id, AttackersWin(Result(3, 202), Result(1, 201)));

        var r4 = result.Rewards.Single(r => r.UserId == 4);
        Assert.False(r4.PresentAtEnd);
        Assert.Equal(0, r4.Coins);
        var match = await _service.GetByIdAsync(id);
        Assert.Equal(match!.EndedAt, match.Participants.Single(p => p.UserId == 4).LeftAt);
        Assert.Equal(10, (await UserAsync(4)).Coins);
    }

    [Fact]
    public async Task Complete_WithoutAStartCall_RecordsTheReportedParticipants()
    {
        var created = await _service.CreateAsync(new SiegeMatchCreateDto { SiegeLobbyId = 1, SiegeScenarioId = 100 });
        _context.ChangeTracker.Clear();

        var result = await _service.CompleteAsync(created.Id, AttackersWin(Result(3, 202), Result(1, 201)));

        Assert.Equal(2, result.Rewards.Count);
        Assert.Equal(250, result.Rewards.Single(r => r.UserId == 3).Coins);
        Assert.Equal(2, (await _service.GetByIdAsync(created.Id))!.Participants.Count);
    }

    [Fact]
    public async Task Complete_Draw_NoWinRewards_ButHoldingAndCaptureStillPaid()
    {
        var id = await StartedMatchAsync((3, 202), (1, 201));
        var dto = new SiegeMatchCompleteDto
        {
            EndReason = SiegeMatchEndReason.TimeExpired,
            WinningAllianceGroup = null,
            Participants = { Result(3, 202), Result(1, 201) },
            Objectives =
            {
                new SiegeMatchObjectiveResultInputDto { SiegeObjectiveId = 501, FinalHolderTeamId = 201 },
                new SiegeMatchObjectiveResultInputDto { SiegeObjectiveId = 502, FinalHolderTeamId = 202, CapturedByUserId = 3 }
            }
        };

        var result = await _service.CompleteAsync(id, dto);

        Assert.Null(result.WinningAllianceGroup);
        Assert.Equal((false, 100, 10, 0), (result.Rewards.Single(r => r.UserId == 3).Won, result.Rewards.Single(r => r.UserId == 3).Coins,
            result.Rewards.Single(r => r.UserId == 3).Experience, result.Rewards.Single(r => r.UserId == 3).Gems));
        Assert.Equal(0, result.Rewards.Single(r => r.UserId == 1).Coins);
    }

    [Fact]
    public async Task Complete_InvalidInput_IsRejectedWithoutChangingAnything()
    {
        var id = await StartedMatchAsync((3, 202), (1, 201));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.CompleteAsync(id, new SiegeMatchCompleteDto
        { EndReason = SiegeMatchEndReason.AdminStopped }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CompleteAsync(id, new SiegeMatchCompleteDto
        { EndReason = SiegeMatchEndReason.TimeExpired, WinningAllianceGroup = 9 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CompleteAsync(id, new SiegeMatchCompleteDto
        {
            EndReason = SiegeMatchEndReason.TimeExpired,
            Objectives = { new SiegeMatchObjectiveResultInputDto { SiegeObjectiveId = 999, FinalHolderTeamId = 201 } }
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CompleteAsync(id, new SiegeMatchCompleteDto
        {
            EndReason = SiegeMatchEndReason.TimeExpired,
            Participants = { Result(3, 999) }
        }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.CompleteAsync(999, AttackersWin()));

        _context.ChangeTracker.Clear();
        Assert.Equal(SiegeMatchStatus.InProgress, (await _service.GetByIdAsync(id))!.Status);
    }

    // ---- abort ----

    [Fact]
    public async Task Abort_GrantsNothing_ClosesParticipants_AndIsIdempotent()
    {
        var id = await StartedMatchAsync((3, 202), (1, 201));

        var aborted = await _service.AbortAsync(id, new SiegeMatchAbortDto { EndReason = SiegeMatchEndReason.AdminStopped });
        _context.ChangeTracker.Clear();
        var again = await _service.AbortAsync(id, new SiegeMatchAbortDto { EndReason = SiegeMatchEndReason.ServerRestart });

        Assert.Equal(SiegeMatchStatus.Aborted, aborted.Status);
        Assert.Equal(SiegeMatchEndReason.AdminStopped, again.EndReason); // the first abort's reason stays
        Assert.Null(again.WinningAllianceGroup);
        Assert.All(again.Participants, p =>
        {
            Assert.NotNull(p.LeftAt);
            Assert.Equal((0, 0, 0), (p.CoinsAwarded, p.ExpAwarded, p.GemsAwarded));
        });
        var u3 = await UserAsync(3);
        Assert.Equal((10, 0, 0), (u3.Coins, u3.Gems, u3.ExperiencePoints));
    }

    [Fact]
    public async Task Abort_And_Complete_DoNotOverrideEachOther()
    {
        var abortedId = await StartedMatchAsync((3, 202), (1, 201));
        await _service.AbortAsync(abortedId, new SiegeMatchAbortDto { EndReason = SiegeMatchEndReason.ServerRestart });
        _context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CompleteAsync(abortedId, AttackersWin(Result(3, 202))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.StartAsync(abortedId, new SiegeMatchStartDto()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ParticipantLeftAsync(abortedId, 3, null));
        Assert.Equal(10, (await UserAsync(3)).Coins);

        var completedId = await StartedMatchAsync((3, 202), (1, 201));
        await _service.CompleteAsync(completedId, AttackersWin(Result(3, 202), Result(1, 201)));
        _context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AbortAsync(completedId, new SiegeMatchAbortDto()));
        Assert.Equal(SiegeMatchStatus.Completed, (await _service.GetByIdAsync(completedId))!.Status);
    }

    [Fact]
    public async Task AbortUnfinished_AbortsCreatedAndInProgress_LeavesFinishedMatchesAlone()
    {
        var created = (await _service.CreateAsync(new SiegeMatchCreateDto { SiegeLobbyId = 1, SiegeScenarioId = 100 })).Id;
        var running = await StartedMatchAsync((3, 202), (1, 201));
        var completed = await StartedMatchAsync((4, 202), (2, 201));
        await _service.CompleteAsync(completed, AttackersWin(Result(4, 202), Result(2, 201)));
        _context.ChangeTracker.Clear();

        var result = await _service.AbortUnfinishedAsync(new SiegeMatchAbortUnfinishedDto());

        Assert.Equal(new[] { created, running }, result.AbortedMatchIds.OrderBy(x => x));
        _context.ChangeTracker.Clear();
        Assert.Equal(SiegeMatchEndReason.ServerRestart, (await _service.GetByIdAsync(running))!.EndReason);
        Assert.Equal(SiegeMatchStatus.Completed, (await _service.GetByIdAsync(completed))!.Status);
        Assert.Empty((await _service.AbortUnfinishedAsync(new SiegeMatchAbortUnfinishedDto())).AbortedMatchIds);
    }

    // ---- history ----

    [Fact]
    public async Task Query_FiltersByUserLobbyAndStatus_NewestFirst()
    {
        var first = await StartedMatchAsync((3, 202), (1, 201));
        await _service.CompleteAsync(first, AttackersWin(Result(3, 202), Result(1, 201)));
        var second = await StartedMatchAsync((4, 202), (1, 201));
        _context.ChangeTracker.Clear();

        var forUser3 = await _service.QueryAsync(3, null, null, 0);
        Assert.Equal(new[] { first }, forUser3.Select(m => m.Id));
        Assert.Equal(250, forUser3[0].Participant!.CoinsAwarded);
        Assert.Equal(2, forUser3[0].ParticipantCount);

        var forUser1 = await _service.QueryAsync(1, 1, null, 0);
        Assert.Equal(new[] { second, first }, forUser1.Select(m => m.Id));

        var inProgress = await _service.QueryAsync(null, null, SiegeMatchStatus.InProgress, 10);
        Assert.Equal(new[] { second }, inProgress.Select(m => m.Id));
        Assert.Null(inProgress[0].Participant);

        Assert.Empty(await _service.QueryAsync(null, 2, null, 10));
    }
}
