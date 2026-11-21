using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services.Logistics;
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Tests.Services.Production;
using KnKWebAPI.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static knkwebapi_v2.Tests.Services.Logistics.LogisticsTestDb;

namespace knkwebapi_v2.Tests.Services.Logistics;

/// <summary>
/// P9 API follow-ups (PandiO/knk-workspace#928, found by L24 in #686): a town's size and specialization with the template
/// that fits them, template labels kept on a PUT that leaves them out, the structure type and the missing item and
/// storage names on the planner's reads, and the townId lookup filter on storages.
/// </summary>
public class LogisticsFollowUpsTests : IDisposable
{
    private readonly LogisticsTestDb _l = new();

    public void Dispose() => _l.Dispose();

    private ILogisticsPolicyService P => _l.Policies;
    private TransportTestDb T => _l.T;
    private const int Phoenix = ProductionWorldTestDb.Phoenix;

    private Task<LogisticsPolicyDto> Template(string name, string? size, string? spec, bool isDefault = false) =>
        P.CreateAsync(new LogisticsPolicyDto { Name = name, Scope = "Template", TownSize = size, Specialization = spec, IsDefaultTemplate = isDefault, Rules = new() }, 7);

    // ================= 1. Town size and specialization =================

    [Fact]
    public async Task ATownsProfile_StartsEmpty_AndSuggestsTheTemplateForItsSizeAndSpecialization()
    {
        await _l.SetUpAsync();
        var empty = await P.GetTownProfileAsync(Phoenix);
        Assert.Equal((Phoenix, "Phoenix", (string?)null, (string?)null, "None", (int?)null),
            (empty.TownId, empty.TownName, empty.TownSize, empty.Specialization, empty.SuggestedTemplateMatch, empty.SuggestedTemplateId));

        var general = await Template("General", null, null, isDefault: true);
        var village = await Template("Village", "Village", null);
        var farming = await Template("Farming village", "Village", "Crops");
        Assert.Equal(("Default", general.Id), ((await P.GetTownProfileAsync(Phoenix)).SuggestedTemplateMatch, (await P.GetTownProfileAsync(Phoenix)).SuggestedTemplateId));

        var set = await P.UpdateTownProfileAsync(Phoenix, new TownLogisticsProfileDto { TownSize = "village", Specialization = "crops" });
        Assert.Equal(("Village", "Crops", "Exact", farming.Id, "Farming village"),
            (set.TownSize, set.Specialization, set.SuggestedTemplateMatch, set.SuggestedTemplateId, set.SuggestedTemplateName));

        var fish = await P.UpdateTownProfileAsync(Phoenix, new TownLogisticsProfileDto { Specialization = "Fish" }); // size left out: kept
        Assert.Equal(("Village", "Fish", "Size", village.Id), (fish.TownSize, fish.Specialization, fish.SuggestedTemplateMatch, fish.SuggestedTemplateId));

        var city = await P.UpdateTownProfileAsync(Phoenix, new TownLogisticsProfileDto { TownSize = "City" });
        Assert.Equal(("City", "Fish", "Default", general.Id), (city.TownSize, city.Specialization, city.SuggestedTemplateMatch, city.SuggestedTemplateId));

        var cleared = await P.UpdateTownProfileAsync(Phoenix, new TownLogisticsProfileDto { TownSize = "", Specialization = " " });
        Assert.Equal(((string?)null, (string?)null), (cleared.TownSize, cleared.Specialization));
        var stored = _l.E.Db.Towns.AsNoTracking().Single(t => t.Id == Phoenix);
        Assert.Equal(((LogisticsTownSize?)null, (ResourceCategory?)null), (stored.LogisticsTownSize, stored.LogisticsSpecialization));

        // Other towns are untouched; a superseded template version is never suggested.
        await P.UpdateTownProfileAsync(Phoenix, new TownLogisticsProfileDto { TownSize = "Village", Specialization = "Crops" });
        var v2 = await P.UpdateAsync(farming.Id!.Value, new LogisticsPolicyDto { Rules = new() }, 7);
        Assert.Equal(v2.Id, (await P.GetTownProfileAsync(Phoenix)).SuggestedTemplateId);
        Assert.Null((await P.GetTownProfileAsync(ProductionWorldTestDb.Rivertown)).TownSize);
    }

    [Fact]
    public async Task TheProfileEndpoints_Answer404ForAnUnknownTown_And400ForAnUnknownLabel()
    {
        await _l.SetUpAsync();
        var c = new LogisticsPoliciesController(_l.Policies) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<OkObjectResult>(await c.TownProfile(Phoenix, default));
        Assert.IsType<NotFoundObjectResult>(await c.TownProfile(99999, default));
        Assert.IsType<NotFoundObjectResult>(await c.UpdateTownProfile(99999, new TownLogisticsProfileDto { TownSize = "City" }, default));
        Assert.IsType<BadRequestObjectResult>(await c.UpdateTownProfile(Phoenix, new TownLogisticsProfileDto { TownSize = "Metropolis" }, default));
        Assert.IsType<BadRequestObjectResult>(await c.UpdateTownProfile(Phoenix, new TownLogisticsProfileDto { Specialization = "Gold" }, default));
        Assert.IsType<BadRequestObjectResult>(await c.UpdateTownProfile(Phoenix, new TownLogisticsProfileDto { TownId = Phoenix + 1 }, default));
        Assert.IsType<BadRequestResult>(await c.UpdateTownProfile(Phoenix, null!, default));
        Assert.Null((await P.GetTownProfileAsync(Phoenix)).TownSize); // nothing written by the refused calls
    }

    // ================= 2. Template labels on PUT =================

    [Fact]
    public async Task APutWithoutLabels_KeepsThem_AndAnEmptyStringClearsThem()
    {
        await _l.SetUpAsync();
        var t = await Template("Farming village", "Village", "Crops", isDefault: true);

        var kept = await P.UpdateAsync(t.Id!.Value, new LogisticsPolicyDto { Reason = "Rules only", Rules = new() { Reserve(Flour, units: 10) } }, 7);
        Assert.Equal(("Farming village", "Village", "Crops", true), (kept.Name, kept.TownSize, kept.Specialization, kept.IsDefaultTemplate!.Value));

        var changed = await P.UpdateAsync(kept.Id!.Value, new LogisticsPolicyDto { Specialization = "Fish", Rules = new() }, 7);
        Assert.Equal(("Village", "Fish"), (changed.TownSize, changed.Specialization));

        var cleared = await P.UpdateAsync(changed.Id!.Value, new LogisticsPolicyDto { Specialization = "", TownSize = "", IsDefaultTemplate = false, Rules = new() }, 7);
        Assert.Equal(((string?)null, (string?)null, false), (cleared.TownSize, cleared.Specialization, cleared.IsDefaultTemplate!.Value));

        await Assert.ThrowsAsync<ArgumentException>(() => P.UpdateAsync(cleared.Id!.Value, new LogisticsPolicyDto { TownSize = "Metropolis", Rules = new() }, 7));
        // A town policy still refuses template labels, and an empty one is fine.
        var town = await _l.TownPolicy();
        await Assert.ThrowsAsync<ArgumentException>(() => P.UpdateAsync(town.Id!.Value, new LogisticsPolicyDto { TownSize = "City", Rules = new() }, 7));
        Assert.Null((await P.UpdateAsync(town.Id!.Value, new LogisticsPolicyDto { TownSize = "", Rules = new() }, 7)).TownSize);
    }

    // ================= 3 and 4. Structure type and names =================

    [Fact]
    public async Task ThePlannersReads_CarryTheStructureType_AndTheItemAndStorageNames()
    {
        await _l.SetUpAsync();
        await T.Put(T.NorthStorage, 100, Flour);
        await _l.MinMax(_l.BakeryInput, Flour, 20, 60);
        string inputName = _l.E.Db.Storages.AsNoTracking().Single(s => s.Id == _l.BakeryInput).Name;
        string northName = _l.E.Db.Storages.AsNoTracking().Single(s => s.Id == T.NorthStorage).Name;

        var dry = await _l.DryRun();
        var e = Entry(dry, _l.BakeryInput, Flour);
        Assert.Equal(LogisticsStructureTypes.ProductionStructure, e.ConsumerStructureType);
        var order = Assert.Single(dry.Orders);
        Assert.Equal((LogisticsStructureTypes.ProductionStructure, inputName, northName), (order.ConsumerStructureType, order.ConsumerStorageName, order.WarehouseStorageName));
        var line = Assert.Single(order.Lines);
        Assert.Equal((Flour, 60L, e.ItemBlueprintName), (line.ItemBlueprintId, line.Amount, line.ItemBlueprintName));
        Assert.False(string.IsNullOrEmpty(line.ItemBlueprintName));

        var plan = await _l.Run();
        var created = Assert.Single(_l.Distribution()); // the order still gets its plain lines
        Assert.Equal((Flour, 60L), (created.Lines.Single().ItemBlueprintId, created.Lines.Single().Requested));
        Assert.Equal(e.ItemBlueprintName, Assert.Single(plan.Orders).Lines.Single().ItemBlueprintName);

        ILogisticsReservationService reservations = _l.Records;
        var row = Assert.Single((await reservations.SearchAsync(new PagedQueryDto { Filters = new() { ["townId"] = Phoenix.ToString() } })).Items);
        Assert.Equal((LogisticsStructureTypes.ProductionStructure, inputName, T.NorthStorage, northName),
            (row.ConsumerStructureType, row.ConsumerStorageName, row.WarehouseStorageId, row.WarehouseStorageName));
        var detail = (await reservations.GetByIdAsync(row.Id))!;
        Assert.Equal(LogisticsStructureTypes.ProductionStructure, detail.ConsumerStructureType);

        ILogisticsPlannerRunService runs = _l.Records;
        var run = (await runs.GetByIdAsync(plan.RunId!.Value))!;
        Assert.All(run.Entries, x => Assert.Equal(LogisticsStructureTypes.ProductionStructure, x.ConsumerStructureType));

        var consumer = (await _l.Planner.ConsumerAsync(_l.Bakery))!;
        Assert.Equal(LogisticsStructureTypes.ProductionStructure, consumer.StructureType);
        Assert.Equal(inputName, Assert.Single(consumer.OpenReservations).ConsumerStorageName);
        Assert.Equal(LogisticsStructureTypes.Warehouse, (await _l.Planner.ConsumerAsync(T.North))!.StructureType);

        var areas = await _l.Planner.ServiceAreasAsync(Phoenix);
        Assert.Equal(LogisticsStructureTypes.ProductionStructure, areas.Entries.Single(a => a.StructureId == _l.Bakery).StructureType);
        Assert.All(areas.Entries.Where(a => a.AreaKind != "Structure"), a => Assert.Null(a.StructureType));
    }

    [Fact]
    public async Task StructureTypes_NameTheMostSpecificType_AndSkipUnknownIds()
    {
        await _l.SetUpAsync();
        var types = await LogisticsStructureTypes.ResolveAsync(_l.E.Db, new[] { _l.Bakery, T.North, 99999, _l.Bakery });
        Assert.Equal(new Dictionary<int, string> { [_l.Bakery] = "ProductionStructure", [T.North] = "Warehouse" }, types);
        Assert.Empty(await LogisticsStructureTypes.ResolveAsync(_l.E.Db, Array.Empty<int>()));
    }

    [Fact]
    public async Task AShop_IsNamedShop_NotProductionStructure()
    {
        using var shops = new knkwebapi_v2.Tests.Services.Shops.ShopTestDb();
        await shops.SetUpAsync(logistics: true);
        var types = await LogisticsStructureTypes.ResolveAsync(shops.E.Db, new[] { shops.Bakery, shops.L.T.North });
        Assert.Equal((LogisticsStructureTypes.Shop, LogisticsStructureTypes.Warehouse), (types[shops.Bakery], types[shops.L.T.North]));
    }

    // ================= 5. townId lookup filter =================

    [Fact]
    public async Task TheStorageSearch_FiltersByTown_ThroughItsDistrictsAndStructures()
    {
        await _l.SetUpAsync();
        var rural = await _l.ConsumerAsync("Farm", 0, districtId: null);
        var storages = new StorageService(_l.E.Db);
        var phoenix = (await storages.SearchAsync(new PagedQueryDto { PageSize = 100, Filters = new() { ["townId"] = Phoenix.ToString() } })).Items.Select(s => s.Id).ToList();
        Assert.Contains(_l.BakeryInput, phoenix);
        Assert.Contains(T.NorthStorage, phoenix);
        Assert.Contains(_l.InputOf(rural), phoenix); // a rural structure of the town
        var elsewhere = await storages.SearchAsync(new PagedQueryDto { PageSize = 100, Filters = new() { ["townId"] = ProductionWorldTestDb.Rivertown.ToString() } });
        Assert.DoesNotContain(elsewhere.Items, s => phoenix.Contains(s.Id));
    }
}
