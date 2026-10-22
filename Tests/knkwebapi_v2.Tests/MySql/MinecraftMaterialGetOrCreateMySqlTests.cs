using AutoMapper;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.MinecraftCatalog;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// KNG-200 phase 1 on a real MySQL: parallel get-or-create calls for one key in different notations
/// (two quick picks in the web app) end with one row, and every caller gets it - no 500.
/// </summary>
[Trait("Category", "requires-mysql")]
public class MinecraftMaterialGetOrCreateMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    private readonly IMapper _mapper = new MapperConfiguration(cfg =>
    {
        cfg.AddProfile<PagedQueryMappingProfile>();
        cfg.AddProfile<MinecraftMaterialRefMappingProfile>();
    }).CreateMapper();

    public MinecraftMaterialGetOrCreateMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    [MySqlFact]
    public async Task ParallelGetOrCreate_OfOneKey_GivesEveryCallerTheSameRow()
    {
        var notations = new[] { "minecraft:race_block", "RACE_BLOCK", " Minecraft:Race_Block ", "race_block" };

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async i =>
        {
            await using var context = _db.NewContext();
            var service = new MinecraftMaterialRefService(new MinecraftMaterialRefRepository(context), _mapper);
            return await service.GetOrCreateAsync(notations[i % notations.Length], "ITEM");
        }));

        Assert.Single(results.Select(r => r.Id).Distinct());
        await using var check = _db.NewContext();
        Assert.Equal(1, await check.MinecraftMaterialRefs.CountAsync(m => m.NamespaceKey == "minecraft:race_block"));
    }
}
