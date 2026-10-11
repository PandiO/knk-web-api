using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Hub DESIGN §13 and §16.4: a domain is only hub-eligible by its flag, flagged domains are never nested, the selected
/// hub must be eligible with a spawn in its world, and the mode exemptions need the group's effective node (KNG-116).
/// </summary>
public class HubSettingsServiceTests : IDisposable
{
    private readonly KnKDbContext _context;
    private readonly Mock<IPermissionResolutionService> _permissions = new();
    private readonly HubSettingsService _hub;

    public HubSettingsServiceTests()
    {
        _context = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _hub = new HubSettingsService(_context, _permissions.Object);
        _context.Towns.Add(new Town { Id = 1, Name = "Hubtown", Description = "", WgRegionId = "town_1", WorldName = "hub" });
        _context.Districts.Add(new District { Id = 2, Name = "Square", Description = "", WgRegionId = "district_2", TownId = 1, WorldName = "hub" });
        _context.Structures.Add(new Structure { Id = 3, Name = "Inn", Description = "", WgRegionId = "domain_3", DistrictId = 2, WorldName = "hub" });
        _context.Towns.Add(new Town { Id = 4, Name = "Oakhaven", Description = "", WgRegionId = "town_4", WorldName = "world" });
        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    private static HubSettingsDto Hub(int? domainId, string world = "hub") => new()
    {
        DomainId = domainId,
        SpawnReference = new LocationReferenceDto
        {
            SourceType = "Location",
            Location = new LocationSnapshotDto { X = 2.5, Y = 65, Z = 2.5, World = world }
        }
    };

    [Fact]
    public async Task FlaggedDomainsAreNeverNested()
    {
        await _hub.SetEligibleAsync(2, true);

        var town = await Assert.ThrowsAsync<ArgumentException>(() => _hub.SetEligibleAsync(1, true));
        Assert.Contains("nested", town.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => _hub.SetEligibleAsync(3, true));

        Assert.True((await _hub.SetEligibleAsync(4, true))!.IsHub, "another town is not nested");
        Assert.Equal(new[] { "Oakhaven", "Square" }, (await _hub.ListEligibleAsync()).Select(d => d.Name));
        Assert.Null(await _hub.SetEligibleAsync(99, true));
    }

    [Fact]
    public async Task TheSelectedHubMustBeEligibleWithItsSpawnInItsWorld()
    {
        var notFlagged = await Assert.ThrowsAsync<ArgumentException>(() => _hub.NormalizeAsync(Hub(1)));
        Assert.Contains("not hub-eligible", notFlagged.Message);

        await _hub.SetEligibleAsync(1, true);
        var wrongWorld = await Assert.ThrowsAsync<ArgumentException>(() => _hub.NormalizeAsync(Hub(1, "world")));
        Assert.Contains("is in world 'world', but 'Hubtown' is in world 'hub'", wrongWorld.Message);

        var noSpawn = Hub(1);
        noSpawn.SpawnReference = null;
        await Assert.ThrowsAsync<ArgumentException>(() => _hub.NormalizeAsync(noSpawn));

        var badAction = Hub(1);
        badAction.OutageAction = "Explode";
        await Assert.ThrowsAsync<ArgumentException>(() => _hub.NormalizeAsync(badAction));

        var ok = Hub(1);
        ok.OutageAction = "kick";
        Assert.Equal("Kick", (await _hub.NormalizeAsync(ok)).OutageAction);
        Assert.Null((await _hub.NormalizeAsync(new HubSettingsDto())).DomainId);
    }

    [Fact]
    public async Task TheSelectedHubCannotBeUnflagged()
    {
        await _hub.SetEligibleAsync(1, true);
        _context.GameSettings.Add(new GameSettings { HubSettingsJson = "{\"domainId\":1}" });
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _hub.SetEligibleAsync(1, false));
        Assert.True((await _hub.ListEligibleAsync()).Single().Selected);
        var described = await _hub.DescribeAsync(new HubSettingsDto { DomainId = 1 });
        Assert.Equal(("Hubtown", "Town", "hub", "town_1"), (described.DomainName, described.DomainType, described.WorldName, described.WgRegionId));
    }

    [Fact]
    public async Task AnExemptionNeedsTheGroupsEffectiveModeNode()
    {
        _permissions.Setup(p => p.CheckGroupAsync(7, HubSettingsService.StaffModeNode))
            .ReturnsAsync(new PermissionGroupCheckResponseDto { GroupId = 7, Result = PermissionResolutionResult.Granted });
        _permissions.Setup(p => p.CheckGroupAsync(7, HubSettingsService.OwnerModeNode))
            .ReturnsAsync(new PermissionGroupCheckResponseDto { GroupId = 7, Result = PermissionResolutionResult.Undeclared });

        await _hub.ValidateExemptionsAsync(new[] { new PermissionGroupGameSettingsDto { PermissionGroupId = 7, ExemptInStaffMode = true } });
        var owner = await Assert.ThrowsAsync<ArgumentException>(() => _hub.ValidateExemptionsAsync(
            new[] { new PermissionGroupGameSettingsDto { PermissionGroupId = 7, ExemptInOwnerMode = true } }));
        Assert.Contains("knk.mode.owner", owner.Message);
        // Decision 5: an unticked box is "not set" and needs nothing.
        await _hub.ValidateExemptionsAsync(new[] { new PermissionGroupGameSettingsDto { PermissionGroupId = 7, ExemptInOwnerMode = false } });
    }

    [Fact]
    public async Task AGroupInheritsItsParentsModeNodeAndADenyStopsIt()
    {
        var grants = new Mock<IPermissionGrantRepository>();
        var groups = new Mock<IPermissionGroupRepository>();
        var staff = new PermissionGroup { Id = 1, Name = "Staff" };
        var moderator = new PermissionGroup { Id = 2, Name = "Moderator", ParentGroupId = 1 };
        var trainee = new PermissionGroup { Id = 3, Name = "Trainee", ParentGroupId = 2 };
        groups.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { staff, moderator, trainee });
        grants.Setup(r => r.GetActiveGrantsForHolderAsync(1, It.IsAny<DateTime>()))
            .ReturnsAsync(new List<PermissionGrant> { new() { HolderId = 1, Node = "knk.mode.*", Value = true } });
        grants.Setup(r => r.GetActiveGrantsForHolderAsync(2, It.IsAny<DateTime>())).ReturnsAsync(new List<PermissionGrant>());
        grants.Setup(r => r.GetActiveGrantsForHolderAsync(3, It.IsAny<DateTime>()))
            .ReturnsAsync(new List<PermissionGrant> { new() { HolderId = 3, Node = "knk.mode.owner", Value = false } });
        var resolution = new PermissionResolutionService(new Mock<IUserRepository>().Object, grants.Object, groups.Object);

        var moderatorStaff = await resolution.CheckGroupAsync(2, "knk.mode.staff");
        Assert.True(moderatorStaff!.Allowed);
        Assert.Equal("Staff", moderatorStaff.SourceGroupName);
        Assert.True((await resolution.CheckGroupAsync(3, "knk.mode.staff"))!.Allowed);
        Assert.False((await resolution.CheckGroupAsync(3, "knk.mode.owner"))!.Allowed, "the trainee's own deny wins");
        Assert.Null(await resolution.CheckGroupAsync(99, "knk.mode.staff"));
    }
}
