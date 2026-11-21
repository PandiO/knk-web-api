using System.Reflection;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Logistics;
using knkwebapi_v2.Tests.Services.Production;
using KnKWebAPI.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static knkwebapi_v2.Tests.Services.Logistics.LogisticsTestDb;

namespace knkwebapi_v2.Tests.Services.Logistics;

/// <summary>
/// Town logistics P9 (PandiO/knk-workspace#127, TOWN_LOGISTICS § 3, § 4, § 4.1; smoke § 10.1): versioned policies (a save
/// is a new version, one current version per town), the V1 rule types only (the Later ones are #324), default templates
/// per specialization (D-L3) applied to a town or used when it has none, consumer replenishment settings, and the
/// controllers' access rules and error mapping.
/// </summary>
public class LogisticsPolicyTests : IDisposable
{
    private readonly LogisticsTestDb _l = new();

    public void Dispose() => _l.Dispose();

    private ILogisticsPolicyService P => _l.Policies;

    // ================= Versions =================

    [Fact]
    public async Task ASave_MakesANewVersion_TheOldOneStaysForTheHistory()
    {
        await _l.SetUpAsync();
        var v1 = await _l.TownPolicy(Reserve(Flour, units: 70));
        Assert.Equal((1, true, "Created."), (v1.Version, v1.IsCurrent, v1.Reason));
        Assert.Equal("Reserve 70 Coal, town-wide", v1.Rules!.Single().Summary);

        var v2 = await P.UpdateAsync(v1.Id!.Value, new LogisticsPolicyDto
        {
            Reason = "Bread reserve 14 days", Rules = new() { Reserve(Bread, days: 14m), Reserve(Flour, units: 50) },
        }, actorUserId: 8);
        Assert.Equal((2, true, v1.LineageId, v1.Id, "Bread reserve 14 days", 8), (v2.Version, v2.IsCurrent, v2.LineageId, v2.BasedOnPolicyId, v2.Reason, v2.CreatedByUserId!.Value));
        Assert.Equal(new[] { 1, 2 }, v2.Rules!.Select(r => r.Sequence).ToArray());
        var old = (await P.GetByIdAsync(v1.Id!.Value))!;
        Assert.Equal((false, v2.Id), (old.IsCurrent, old.SupersededByPolicyId));
        Assert.Single(old.Rules!); // a version never changes
        Assert.Equal(new[] { 2, 1 }, (await P.VersionsAsync(v1.Id!.Value)).Select(v => v.Version).ToArray());

        var stale = await Assert.ThrowsAsync<InvalidOperationException>(() => P.UpdateAsync(v1.Id!.Value, new LogisticsPolicyDto { Rules = new() }, null));
        Assert.Contains("not the current version", stale.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _l.TownPolicy()); // one per town
        var search = await P.SearchAsync(new PagedQueryDto { Filters = new() { ["townId"] = ProductionWorldTestDb.Phoenix.ToString() } });
        Assert.Equal(v2.Id, Assert.Single(search.Items).Id); // current versions by default

        // The planner uses the current version.
        await T().Put(T().NorthStorage, 100, Flour);
        await _l.MinMax(_l.BakeryInput, Flour, 0, 60);
        var plan = await _l.DryRun();
        Assert.Equal((v2.Id, 2), (plan.PolicyId, plan.PolicyVersion!.Value));
        Assert.Equal(50L, LogisticsTestDb.Entry(plan, _l.BakeryInput, Flour).Reserved);

        await P.DeleteAsync(v2.Id!.Value);
        Assert.Empty(_l.E.Db.Set<LogisticsPolicy>().AsNoTracking()); // every version
        Assert.Empty(_l.E.Db.Set<LogisticsRule>().AsNoTracking());
    }

    [Fact]
    public async Task OnlyTheV1RuleTypes_AndEachRuleIsChecked()
    {
        await _l.SetUpAsync();
        async Task<string> Refused(params LogisticsRuleDto[] rules) =>
            (await Assert.ThrowsAsync<ArgumentException>(() => _l.TownPolicy(rules))).Message;

        Assert.Contains("Later, #324", await Refused(new LogisticsRuleDto { Type = "Ceiling" }));
        Assert.Contains("units or daysOfCover", await Refused(new LogisticsRuleDto { Type = "Reserve", ItemBlueprintId = Flour }));
        Assert.Contains("warehouseId is required", await Refused(new LogisticsRuleDto { Type = "Reserve", Scope = "Warehouse", Units = 1 }));
        Assert.Contains("priorityClass must be", await Refused(new LogisticsRuleDto { Type = "PriorityClass", PriorityClass = "Urgent" }));
        Assert.Contains("areaKind must be", await Refused(new LogisticsRuleDto { Type = "ServiceArea", WarehouseId = T().North }));
        Assert.Contains("a district of this town", await Refused(new LogisticsRuleDto { Type = "ServiceArea", AreaKind = "District", DistrictId = 4242, WarehouseId = T().North }));
        Assert.Contains("reorderPoint ≤ targetLevel", await Refused(new LogisticsRuleDto { Type = "Replenishment", ItemBlueprintId = Flour, ReorderPoint = 9, TargetLevel = 5 }));
        Assert.Contains("activeUntil must be after", await Refused(new LogisticsRuleDto
        {
            Type = "Reserve", Units = 1, ActiveFrom = new DateTime(2026, 12, 2, 0, 0, 0, DateTimeKind.Utc), ActiveUntil = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
        }));
        Assert.Empty(_l.E.Db.Set<LogisticsPolicy>().AsNoTracking());
    }

    [Fact]
    public async Task ARuleOutsideItsWindow_DoesNotApply_AndAReplenishmentRuleSetsDefaultsForAGroup()
    {
        await _l.SetUpAsync();
        await T().Put(T().NorthStorage, 100, Flour);
        var later = _l.E.Clock.GetUtcNow().UtcDateTime.AddDays(1);
        await _l.TownPolicy(
            new LogisticsRuleDto { Type = "Reserve", ItemBlueprintId = Flour, Units = 100, ActiveFrom = later },
            new LogisticsRuleDto { Type = "Replenishment", ConsumerKind = "Structure", StructureId = _l.Bakery, ItemBlueprintId = Flour, ReorderPoint = 5, TargetLevel = 25 });
        var e = LogisticsTestDb.Entry(await _l.DryRun(), _l.BakeryInput, Flour);
        Assert.Equal(("Rule", 25L, "Reserved"), (e.SettingSource, e.Reserved, e.Outcome)); // the reserve starts tomorrow
        await _l.MinMax(_l.BakeryInput, Flour, 0, 10); // the storage's own setting wins
        Assert.Equal(10L, LogisticsTestDb.Entry(await _l.DryRun(), _l.BakeryInput, Flour).Reserved);
    }

    // ================= Templates (D-L3) =================

    [Fact]
    public async Task ADefaultTemplate_IsUsedWithoutATownPolicy_AndApplyingOneCopiesItsRules()
    {
        await _l.SetUpAsync();
        var refused = await Assert.ThrowsAsync<ArgumentException>(() => P.CreateAsync(new LogisticsPolicyDto
        {
            Name = "Bad", Scope = "Template", Rules = new() { new LogisticsRuleDto { Type = "ServiceArea", AreaKind = "Rural", WarehouseId = T().North } },
        }, null));
        Assert.Contains("no warehouse rules or service areas", refused.Message);

        var farming = await P.CreateAsync(new LogisticsPolicyDto
        {
            Name = "Farming village", Scope = "Template", Specialization = "crops", TownSize = "Village", IsDefaultTemplate = true,
            Rules = new() { Reserve(Flour, units: 80) },
        }, null);
        Assert.Equal(("Crops", "Village", true), (farming.Specialization, farming.TownSize, farming.IsDefaultTemplate!.Value));
        var mining = await P.CreateAsync(new LogisticsPolicyDto
        {
            Name = "Mining town", Scope = "Template", Specialization = "Ore", IsDefaultTemplate = true, Rules = new() { Reserve(Flour, units: 90) },
        }, null);
        Assert.False((await P.GetByIdAsync(farming.Id!.Value))!.IsDefaultTemplate); // one default

        await T().Put(T().NorthStorage, 100, Flour);
        await _l.MinMax(_l.BakeryInput, Flour, 0, 60);
        var plan = await _l.DryRun();
        Assert.Equal(("DefaultTemplate", mining.Id), (plan.PolicySource, plan.PolicyId));
        Assert.Equal(10L, LogisticsTestDb.Entry(plan, _l.BakeryInput, Flour).Reserved);

        var town = await P.ApplyTemplateAsync(farming.Id!.Value, new LogisticsApplyTemplateDto { TownId = ProductionWorldTestDb.Phoenix }, 7);
        Assert.Equal(("Town", 1, farming.Id, "Reserve 80 Coal, town-wide"), (town.Scope, town.Version, town.BasedOnPolicyId, town.Rules!.Single().Summary));
        Assert.Contains("From template \"Farming village\" v1", town.Reason);
        var again = await P.ApplyTemplateAsync(mining.Id!.Value, new LogisticsApplyTemplateDto { TownId = ProductionWorldTestDb.Phoenix, Reason = "Now mining" }, 7);
        Assert.Equal((2, town.LineageId, "Now mining"), (again.Version, again.LineageId, again.Reason));
        Assert.Equal("Town", (await _l.DryRun()).PolicySource);
        await Assert.ThrowsAsync<ArgumentException>(() => P.ApplyTemplateAsync(town.Id!.Value, new LogisticsApplyTemplateDto { TownId = ProductionWorldTestDb.Phoenix }, 7));
    }

    [Fact]
    public async Task AWarehousePolicy_HoldsLocalReservesForItsWarehouse()
    {
        await _l.SetUpAsync();
        var local = await P.CreateAsync(new LogisticsPolicyDto
        {
            Name = "North keeps", Scope = "Warehouse", WarehouseId = T().North, Rules = new() { new LogisticsRuleDto { Type = "Reserve", ItemBlueprintId = Flour, Units = 95 } },
        }, null);
        Assert.Equal(("Warehouse", T().North), (local.Rules!.Single().Scope, local.Rules!.Single().WarehouseId!.Value));
        await Assert.ThrowsAsync<ArgumentException>(() => P.UpdateAsync(local.Id!.Value, new LogisticsPolicyDto
        {
            Rules = new() { Priority(_l.Bakery, "High") },
        }, null));
        await T().Put(T().NorthStorage, 100, Flour);
        await _l.MinMax(_l.BakeryInput, Flour, 0, 60);
        Assert.Equal(5L, LogisticsTestDb.Entry(await _l.DryRun(), _l.BakeryInput, Flour).Reserved);
        var rules = await P.SearchRulesAsync(new PagedQueryDto { Filters = new() { ["logisticsPolicyId"] = local.Id!.Value.ToString() } });
        Assert.Equal("Reserve 95 Coal, at North", Assert.Single(rules.Items).Summary);
    }

    // ================= Consumer replenishment (§ 3) =================

    [Fact]
    public async Task ReplenishmentSettings_AreForConsumerStoragesOnly_OnePerBlueprint()
    {
        await _l.SetUpAsync();
        var s = await _l.MinMax(_l.BakeryInput, Flour, 20, 60, "High");
        Assert.Equal((_l.Bakery, "High", true), (s.StructureId!.Value, s.PriorityClass, s.Enabled!.Value));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _l.MinMax(_l.BakeryInput, Flour, 1, 2));
        var warehouse = await Assert.ThrowsAsync<ArgumentException>(() => _l.MinMax(T().NorthStorage, Flour, 1, 2));
        Assert.Contains("Input or Merchandise", warehouse.Message);
        var output = _l.E.Db.Storages.AsNoTracking().Single(x => x.DomainId == _l.Bakery && x.Purpose == StoragePurpose.Output).Id;
        await Assert.ThrowsAsync<ArgumentException>(() => _l.MinMax(output, Flour, 1, 2));
        await Assert.ThrowsAsync<ArgumentException>(() => _l.MinMax(_l.GarrisonInput, Flour, 9, 5));
        var updated = await _l.Settings.UpdateAsync(s.Id!.Value, new ConsumerReplenishmentDto { TargetLevel = 80, PriorityClass = "", Enabled = false });
        Assert.Equal((20L, 80L, (string?)null, false), (updated.ReorderPoint!.Value, updated.TargetLevel!.Value, updated.PriorityClass, updated.Enabled!.Value));
        var page = await _l.Settings.SearchAsync(new PagedQueryDto { Filters = new() { ["townId"] = ProductionWorldTestDb.Phoenix.ToString() } });
        Assert.Equal(("Bakery", "Coal"), (Assert.Single(page.Items).StructureName, page.Items[0].ItemBlueprintName));
        await _l.Settings.DeleteAsync(s.Id!.Value);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _l.Settings.DeleteAsync(s.Id!.Value));
    }

    // ================= Controllers =================

    [Fact]
    public void TheRoutes_CarryTheirAccessRules()
    {
        foreach (var c in new[]
                 {
                     typeof(LogisticsPoliciesController), typeof(LogisticsRulesController), typeof(ConsumerReplenishmentsController),
                     typeof(LogisticsReservationsController), typeof(LogisticsPlannerRunsController), typeof(LogisticsController),
                 })
            Assert.Equal(StaffPermissions.ManageWorld, c.GetCustomAttribute<RequireServiceOrPermissionForWritesAttribute>()!.Node);
        Assert.Equal(StaffPermissions.Delete, typeof(LogisticsPoliciesController).GetMethod("Delete")!.GetCustomAttribute<RequireServiceOrPermissionAttribute>()!.Node);
        foreach (var type in new[] { "LogisticsPolicy", "LogisticsRule", "ConsumerReplenishment", "LogisticsReservation", "LogisticsPlannerRun", "LogisticsPlanEntry" })
            Assert.Equal("World", DashboardAccess.Find(type)!.Area.ToString());
        Assert.Null(DashboardAccess.Find("LogisticsReservation")!.Delete);
        Assert.Null(DashboardAccess.Find("LogisticsPlannerRun")!.Delete);
    }

    [Fact]
    public async Task Results_MapTo400_404_And409WhileOff()
    {
        using var off = new LogisticsTestDb(productionEnabled: false);
        await off.SetUpAsync();
        var policies = new LogisticsPoliciesController(off.Policies) { ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() } };
        Assert.IsType<BadRequestObjectResult>(await policies.Create(new LogisticsPolicyDto { Name = "x", Scope = "Nowhere" }, default));
        Assert.IsType<NotFoundObjectResult>(await policies.Versions(4242, default));
        var logistics = new LogisticsController(off.Planner) { ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() } };
        Assert.IsType<OkObjectResult>(await logistics.DryRun(ProductionWorldTestDb.Phoenix, default));
        var conflict = Assert.IsType<ConflictObjectResult>(await logistics.RunNow(ProductionWorldTestDb.Phoenix, default));
        Assert.Contains("ProductionDisabled", System.Text.Json.JsonSerializer.Serialize(conflict.Value));
        Assert.IsType<NotFoundObjectResult>(await logistics.Supply(4242, default));
        Assert.IsType<NotFoundResult>(await logistics.Consumer(4242, default));
    }

    private TransportTestDb T() => _l.T;
}
