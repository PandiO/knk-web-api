using System.Text.Json;
using AutoMapper;
using Moq;
using Xunit;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// PandiO/knk-workspace#852: a permission group's default /navigate route mode. Validation and storage on write
/// (omitted keeps, "" clears, the three words in any case set, anything else is a 400), and the read DTOs the web
/// app and the plugin (GET api/PermissionGroups) get.
/// </summary>
public class PermissionGroupDefaultRouteModeTests
{
    private static IMapper Mapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<PermissionMappingProfile>()).CreateMapper();

    private static PermissionGroupService GroupService(Mock<IPermissionGroupRepository> repo) => new(repo.Object, Mapper());

    private static (PermissionGroup Existing, Mock<IPermissionGroupRepository> Repo) Stored(string? mode)
    {
        var existing = new PermissionGroup { Id = 7, Name = "Staff", Weight = 40, DefaultRouteMode = mode };
        var repo = new Mock<IPermissionGroupRepository>();
        repo.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(existing);
        return (existing, repo);
    }

    private static PermissionGroupDto Edit(string? mode) => new() { Name = "Staff", Weight = 40, DefaultRouteMode = mode };

    [Fact]
    public void Values_AreThePluginsModeWords()
    {
        Assert.Equal(new[] { "shortest", "efficient", "preferred" }, PermissionGroupRouteMode.Values);
    }

    [Theory]
    [InlineData("shortest", "shortest")]
    [InlineData("efficient", "efficient")]
    [InlineData("preferred", "preferred")]
    [InlineData("Efficient", "efficient")]
    [InlineData(" SHORTEST ", "shortest")]
    public async Task UpdateAsync_AModeWord_IsStoredLowercase(string sent, string stored)
    {
        var (existing, repo) = Stored(null);

        await GroupService(repo).UpdateAsync(7, Edit(sent));

        Assert.Equal(stored, existing.DefaultRouteMode);
        repo.Verify(r => r.UpdateAsync(existing), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_Omitted_KeepsTheStoredDefault()
    {
        var (existing, repo) = Stored("efficient");

        await GroupService(repo).UpdateAsync(7, Edit(null));

        Assert.Equal("efficient", existing.DefaultRouteMode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateAsync_Blank_ClearsTheDefault(string sent)
    {
        var (existing, repo) = Stored("efficient");

        await GroupService(repo).UpdateAsync(7, Edit(sent));

        Assert.Null(existing.DefaultRouteMode);
    }

    [Theory]
    [InlineData("fastest")]
    [InlineData("none")]
    [InlineData("short")]
    [InlineData("preferred,shortest")]
    public async Task UpdateAsync_AnotherWord_IsRefusedAndNothingIsSaved(string sent)
    {
        var (existing, repo) = Stored("efficient");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => GroupService(repo).UpdateAsync(7, Edit(sent)));

        Assert.Contains("DefaultRouteMode", ex.Message);
        Assert.Equal("efficient", existing.DefaultRouteMode);
        repo.Verify(r => r.UpdateAsync(It.IsAny<PermissionGroup>()), Times.Never);
    }

    [Theory]
    [InlineData("Preferred", "preferred")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public async Task CreateAsync_StoresTheDefaultOrNone(string? sent, string? stored)
    {
        var repo = new Mock<IPermissionGroupRepository>();
        PermissionGroup? added = null;
        repo.Setup(r => r.AddAsync(It.IsAny<PermissionGroup>())).Callback<PermissionGroup>(g => added = g);

        var created = await GroupService(repo).CreateAsync(new PermissionGroupDto { Name = "Builders", DefaultRouteMode = sent });

        Assert.NotNull(added);
        Assert.Equal(stored, added!.DefaultRouteMode);
        Assert.Equal(stored, created.DefaultRouteMode);
    }

    [Fact]
    public async Task CreateAsync_AnotherWord_IsRefused()
    {
        var repo = new Mock<IPermissionGroupRepository>();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            GroupService(repo).CreateAsync(new PermissionGroupDto { Name = "Builders", DefaultRouteMode = "walk" }));

        repo.Verify(r => r.AddAsync(It.IsAny<PermissionGroup>()), Times.Never);
    }

    [Theory]
    [InlineData("efficient")]
    [InlineData(null)]
    public async Task GetAllAsync_ThePluginsRead_CarriesTheDefault(string? mode)
    {
        var repo = new Mock<IPermissionGroupRepository>();
        repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { new PermissionGroup { Id = 7, Name = "Staff", Weight = 40, DefaultRouteMode = mode } });

        var dto = Assert.Single(await GroupService(repo).GetAllAsync());

        Assert.Equal(mode, dto.DefaultRouteMode);
    }

    [Fact]
    public void ReadDto_SerializesAsDefaultRouteMode_NullWhenUnset()
    {
        var mapper = Mapper();
        var set = JsonSerializer.Serialize(mapper.Map<PermissionGroupDto>(new PermissionGroup { Id = 1, Name = "Staff", DefaultRouteMode = "efficient" }));
        var unset = JsonSerializer.Serialize(mapper.Map<PermissionGroupDto>(new PermissionGroup { Id = 2, Name = "Default" }));

        Assert.Contains("\"defaultRouteMode\":\"efficient\"", set);
        Assert.Contains("\"defaultRouteMode\":null", unset);
    }

    [Fact]
    public void ListDto_CarriesTheDefault()
    {
        var row = Mapper().Map<PermissionGroupListDto>(new PermissionGroup { Id = 1, Name = "Staff", DefaultRouteMode = "shortest" });

        Assert.Equal("shortest", row.defaultRouteMode);
    }

    [Fact]
    public void WriteMapping_LeavesTheDefaultToTheService()
    {
        var entity = Mapper().Map<PermissionGroup>(new PermissionGroupDto { Name = "Staff", DefaultRouteMode = "efficient" });

        Assert.Null(entity.DefaultRouteMode);
    }
    [Fact]
    public void FormSeed_HasTheFieldAsASelectOfTheThreeWords()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "knkwebapi_v2.csproj"))) dir = dir.Parent;
        using var seed = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "Data", "FormConfigurationSeeds", "PermissionGroup.json")));

        var field = seed.RootElement.GetProperty("steps").EnumerateArray()
            .SelectMany(step => step.GetProperty("fields").EnumerateArray())
            .Single(f => f.GetProperty("fieldName").GetString() == "DefaultRouteMode");
        Assert.Equal("String", field.GetProperty("fieldType").GetString());
        Assert.False(field.GetProperty("isRequired").GetBoolean());
        using var settings = JsonDocument.Parse(field.GetProperty("settingsJson").GetString()!);
        Assert.Equal(PermissionGroupRouteMode.Values,
            settings.RootElement.GetProperty("enumValues").EnumerateArray().Select(v => v.GetString()!).ToArray());
        Assert.Equal("None (default: preferred)", settings.RootElement.GetProperty("emptyOptionLabel").GetString());
    }
}
