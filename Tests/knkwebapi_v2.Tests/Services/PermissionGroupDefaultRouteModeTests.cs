using System.Text.Json;
using System.Text.Json.Serialization;
using AutoMapper;
using Moq;
using Xunit;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Json;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// PandiO/knk-workspace#852: a permission group's default /navigate route mode, an enum <see cref="RouteMode"/> since
/// the developer decision of 2026-11-21. The JSON binding with the app's settings (a name in any case sets it, null or
/// "" clears it, a body without the property keeps the stored value, another word is refused), storage on write, and
/// the read DTOs the web app and the plugin (GET api/PermissionGroups) get.
/// </summary>
public class PermissionGroupDefaultRouteModeTests
{
    /// <summary>
    /// The JSON settings of Program.cs (AddJsonOptions on MVC's web defaults, so names match in any case), so the tests
    /// bind bodies as the API does.
    /// </summary>
    private static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
        Converters = { new NullableEnumConverterFactory(), new JsonStringEnumConverter() },
    };

    private static IMapper Mapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<PermissionMappingProfile>()).CreateMapper();

    private static PermissionGroupService GroupService(Mock<IPermissionGroupRepository> repo) => new(repo.Object, Mapper());

    private static (PermissionGroup Existing, Mock<IPermissionGroupRepository> Repo) Stored(RouteMode? mode)
    {
        var existing = new PermissionGroup { Id = 7, Name = "Staff", Weight = 40, DefaultRouteMode = mode };
        var repo = new Mock<IPermissionGroupRepository>();
        repo.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(existing);
        return (existing, repo);
    }

    /// <summary>A PUT body for Staff, bound as the API binds it; <paramref name="modeJson"/> null leaves the property out.</summary>
    private static PermissionGroupDto Body(string? modeJson) =>
        JsonSerializer.Deserialize<PermissionGroupDto>(
            modeJson == null
                ? "{\"name\":\"Staff\",\"weight\":40}"
                : $"{{\"name\":\"Staff\",\"weight\":40,\"defaultRouteMode\":{modeJson}}}",
            ApiJson)!;

    [Fact]
    public void Values_AreTheEnumNamesInThePluginsOrder()
    {
        Assert.Equal(new[] { "Shortest", "Efficient", "Preferred" }, PermissionGroupRouteMode.Values);
    }

    [Theory]
    [InlineData("\"Shortest\"", RouteMode.Shortest)]
    [InlineData("\"Efficient\"", RouteMode.Efficient)]
    [InlineData("\"Preferred\"", RouteMode.Preferred)]
    [InlineData("\"efficient\"", RouteMode.Efficient)]
    [InlineData("\"SHORTEST\"", RouteMode.Shortest)]
    public async Task UpdateAsync_AModeName_IsStored(string sent, RouteMode stored)
    {
        var (existing, repo) = Stored(null);

        await GroupService(repo).UpdateAsync(7, Body(sent));

        Assert.Equal(stored, existing.DefaultRouteMode);
        repo.Verify(r => r.UpdateAsync(existing), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_Omitted_KeepsTheStoredDefault()
    {
        var (existing, repo) = Stored(RouteMode.Efficient);

        var body = Body(null);
        await GroupService(repo).UpdateAsync(7, body);

        Assert.False(body.DefaultRouteModeSent);
        Assert.Equal(RouteMode.Efficient, existing.DefaultRouteMode);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("null")]
    public async Task UpdateAsync_BlankOrNull_ClearsTheDefault(string sent)
    {
        var (existing, repo) = Stored(RouteMode.Efficient);

        await GroupService(repo).UpdateAsync(7, Body(sent));

        Assert.Null(existing.DefaultRouteMode);
    }

    [Theory]
    [InlineData("{\"Name\":\"Staff\",\"Weight\":40,\"DefaultRouteMode\":\"Shortest\"}", RouteMode.Shortest)]
    [InlineData("{\"Name\":\"Staff\",\"Weight\":40,\"DefaultRouteMode\":\"\"}", null)]
    public async Task UpdateAsync_TheFormsBody_SetsOrClears(string body, RouteMode? stored)
    {
        // The form wizard sends the field under its PascalCase field name, and "" for the "None" option.
        var (existing, repo) = Stored(RouteMode.Efficient);

        await GroupService(repo).UpdateAsync(7, JsonSerializer.Deserialize<PermissionGroupDto>(body, ApiJson)!);

        Assert.Equal(stored, existing.DefaultRouteMode);
    }

    [Theory]
    [InlineData("\"fastest\"")]
    [InlineData("\"none\"")]
    [InlineData("\"short\"")]
    public void Binding_AnotherWord_IsRefused(string sent)
    {
        // The API answers 400 for a body the JSON binding refuses; the service is never called.
        Assert.Throws<JsonException>(() => Body(sent));
    }

    [Fact]
    public async Task UpdateAsync_ANumberThatIsNoMode_IsRefusedAndNothingIsSaved()
    {
        var (existing, repo) = Stored(RouteMode.Efficient);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => GroupService(repo).UpdateAsync(7, Body("7")));

        Assert.Contains("DefaultRouteMode", ex.Message);
        Assert.Equal(RouteMode.Efficient, existing.DefaultRouteMode);
        repo.Verify(r => r.UpdateAsync(It.IsAny<PermissionGroup>()), Times.Never);
    }

    [Theory]
    [InlineData("{\"name\":\"Builders\",\"defaultRouteMode\":\"Preferred\"}", RouteMode.Preferred)]
    [InlineData("{\"name\":\"Builders\",\"defaultRouteMode\":\"\"}", null)]
    [InlineData("{\"name\":\"Builders\"}", null)]
    public async Task CreateAsync_StoresTheDefaultOrNone(string body, RouteMode? stored)
    {
        var repo = new Mock<IPermissionGroupRepository>();
        PermissionGroup? added = null;
        repo.Setup(r => r.AddAsync(It.IsAny<PermissionGroup>())).Callback<PermissionGroup>(g => added = g);

        var created = await GroupService(repo).CreateAsync(JsonSerializer.Deserialize<PermissionGroupDto>(body, ApiJson)!);

        Assert.NotNull(added);
        Assert.Equal(stored, added!.DefaultRouteMode);
        Assert.Equal(stored, created.DefaultRouteMode);
    }

    [Fact]
    public async Task CreateAsync_ANumberThatIsNoMode_IsRefused()
    {
        var repo = new Mock<IPermissionGroupRepository>();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            GroupService(repo).CreateAsync(new PermissionGroupDto { Name = "Builders", DefaultRouteMode = (RouteMode)9 }));

        repo.Verify(r => r.AddAsync(It.IsAny<PermissionGroup>()), Times.Never);
    }

    [Theory]
    [InlineData(RouteMode.Efficient)]
    [InlineData(null)]
    public async Task GetAllAsync_ThePluginsRead_CarriesTheDefault(RouteMode? mode)
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
        var set = JsonSerializer.Serialize(mapper.Map<PermissionGroupDto>(new PermissionGroup { Id = 1, Name = "Staff", DefaultRouteMode = RouteMode.Efficient }), ApiJson);
        var unset = JsonSerializer.Serialize(mapper.Map<PermissionGroupDto>(new PermissionGroup { Id = 2, Name = "Default" }), ApiJson);

        // The plugin reads the name in any case (knk-api-client PermissionGroupsQueryApiImplTest).
        Assert.Contains("\"defaultRouteMode\":\"Efficient\"", set);
        Assert.Contains("\"defaultRouteMode\":null", unset);
        Assert.DoesNotContain("DefaultRouteModeSent", set);
    }

    [Fact]
    public void ListDto_CarriesTheDefault()
    {
        var row = Mapper().Map<PermissionGroupListDto>(new PermissionGroup { Id = 1, Name = "Staff", DefaultRouteMode = RouteMode.Shortest });

        Assert.Equal(RouteMode.Shortest, row.defaultRouteMode);
        Assert.Contains("\"defaultRouteMode\":\"Shortest\"", JsonSerializer.Serialize(row, ApiJson));
    }

    [Fact]
    public void WriteMapping_LeavesTheDefaultToTheService()
    {
        var entity = Mapper().Map<PermissionGroup>(new PermissionGroupDto { Name = "Staff", DefaultRouteMode = RouteMode.Efficient });

        Assert.Null(entity.DefaultRouteMode);
    }

    [Fact]
    public void FormSeed_HasTheFieldAsAnEnumOfTheThreeModes()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "knkwebapi_v2.csproj"))) dir = dir.Parent;
        using var seed = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "Data", "FormConfigurationSeeds", "PermissionGroup.json")));

        var field = seed.RootElement.GetProperty("steps").EnumerateArray()
            .SelectMany(step => step.GetProperty("fields").EnumerateArray())
            .Single(f => f.GetProperty("fieldName").GetString() == "DefaultRouteMode");
        Assert.Equal("Enum", field.GetProperty("fieldType").GetString());
        Assert.False(field.GetProperty("isRequired").GetBoolean());
        using var settings = JsonDocument.Parse(field.GetProperty("settingsJson").GetString()!);
        Assert.Equal(PermissionGroupRouteMode.Values,
            settings.RootElement.GetProperty("enumValues").EnumerateArray().Select(v => v.GetString()!).ToArray());
        Assert.Equal("None (default: Preferred)", settings.RootElement.GetProperty("emptyOptionLabel").GetString());
    }
}
