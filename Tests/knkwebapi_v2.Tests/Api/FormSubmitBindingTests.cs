using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Json;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

// KNG-195: what the web app's form wizard sends, bound with the API's JSON options (Program.cs).
public class FormSubmitBindingTests
{
    private static JsonSerializerOptions ApiJson(bool tolerantEnums = true)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNamingPolicy = null };
        if (tolerantEnums) options.Converters.Add(new NullableEnumConverterFactory());
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static readonly IMapper UserMapper =
        new MapperConfiguration(cfg => cfg.AddProfile<UserMappingProfile>()).CreateMapper();

    private static User FrozenPlayer() => new()
    {
        Id = 7,
        Username = "Steve",
        Email = "steve@example.com",
        IsActive = true,
        IsFrozen = true,
        FrozenReason = "griefing",
        FrozenByUserId = 1,
        FrozenAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        EmailVerified = true,
        AccountCreatedVia = AccountCreationMethod.MinecraftServer,
        Gender = Gender.Female,
        GatePassThroughMethodDefault = GatePassThroughMethod.Teleport,
    };

    // ===== Bug 2: the generic user edit =====

    [Fact]
    public void UserUpdate_FormWithoutStaffFields_KeepsFreezeVerificationAndCreationChannel()
    {
        var dto = JsonSerializer.Deserialize<UserDto>(
            """{"Username":"Steve","Email":"steve@example.com"}""", ApiJson())!;
        var user = FrozenPlayer();

        UserMapper.Map(dto, user);

        Assert.True(user.IsFrozen);
        Assert.Equal("griefing", user.FrozenReason);
        Assert.Equal(1, user.FrozenByUserId);
        Assert.True(user.EmailVerified);
        Assert.Equal(AccountCreationMethod.MinecraftServer, user.AccountCreatedVia);
    }

    [Fact]
    public void UserUpdate_SendingStaffFields_StillDoesNotUnfreeze()
    {
        var dto = JsonSerializer.Deserialize<UserDto>(
            """{"Username":"Steve","Email":"steve@example.com","isFrozen":false,"frozenReason":null,"emailVerified":false,"accountCreatedVia":"WebApp"}""",
            ApiJson())!;
        var user = FrozenPlayer();

        UserMapper.Map(dto, user);

        Assert.True(user.IsFrozen);
        Assert.Equal("griefing", user.FrozenReason);
        Assert.True(user.EmailVerified);
        Assert.Equal(AccountCreationMethod.MinecraftServer, user.AccountCreatedVia);
    }

    [Fact]
    public void UserUpdate_FormWithoutEditableFields_KeepsGenderActiveAndPassThrough()
    {
        var dto = JsonSerializer.Deserialize<UserDto>(
            """{"Username":"Steve","Email":"steve@example.com"}""", ApiJson())!;
        var user = FrozenPlayer();

        UserMapper.Map(dto, user);

        Assert.Equal(Gender.Female, user.Gender);
        Assert.True(user.IsActive);
        Assert.Equal(GatePassThroughMethod.Teleport, user.GatePassThroughMethodDefault);
    }

    [Fact]
    public void UserUpdate_SentEditableFields_AreWritten()
    {
        var dto = JsonSerializer.Deserialize<UserDto>(
            """{"Username":"Steve","Email":"steve@example.com","Gender":null,"IsActive":false,"GatePassThroughMethodDefault":"InstantOpen"}""",
            ApiJson())!;
        var user = FrozenPlayer();

        UserMapper.Map(dto, user);

        Assert.Null(user.Gender);
        Assert.False(user.IsActive);
        Assert.Equal(GatePassThroughMethod.InstantOpen, user.GatePassThroughMethodDefault);
    }

    // ===== Bug 3: "" for a nullable enum =====

    [Fact]
    public void EmptyEnumOption_WithoutTolerantConverter_FailsTheWholeBody()
    {
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<UserDto>(
            """{"Username":"Steve","Gender":""}""", ApiJson(tolerantEnums: false)));
    }

    [Theory]
    [InlineData("\"\"", null)]
    [InlineData("\"  \"", null)]
    [InlineData("null", null)]
    [InlineData("\"Male\"", Gender.Male)]
    [InlineData("\"female\"", Gender.Female)]
    public void EmptyEnumOption_ReadsAsNull_AndNamesStillBind(string json, Gender? expected)
    {
        var dto = JsonSerializer.Deserialize<UserDto>(
            $$"""{"Username":"Steve","Gender":{{json}}}""", ApiJson())!;

        Assert.Equal(expected, dto.Gender);
        Assert.True(dto.HasGender);
    }

    [Fact]
    public void NullableEnum_UnknownName_StillFails()
    {
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<UserDto>(
            """{"Username":"Steve","Gender":"Nonsense"}""", ApiJson()));
    }

    [Fact]
    public void NullableEnum_IsStillWrittenAsName()
    {
        var json = JsonSerializer.Serialize(new UserDto { Username = "Steve", Gender = Gender.Male }, ApiJson());

        Assert.Contains("\"gender\":\"Male\"", json);
        Assert.DoesNotContain("HasGender", json);
    }

    // ===== Bug 4: hybrid material picker key =====

    [Fact]
    public void ItemBlueprint_HybridMaterialKeyNamedAfterTheField_Binds()
    {
        const string body = """{"Name":"Steel Sword","IconMaterialRefId":0,"IconMaterialRefNamespaceKey":"minecraft:iron_sword"}""";

        var create = JsonSerializer.Deserialize<ItemBlueprintCreateDto>(body, ApiJson())!;
        var update = JsonSerializer.Deserialize<ItemBlueprintUpdateDto>(body, ApiJson())!;

        Assert.Equal("minecraft:iron_sword", create.IconNamespaceKey);
        Assert.Equal("minecraft:iron_sword", update.IconNamespaceKey);
    }

    [Fact]
    public void Category_HybridMaterialKeyNamedAfterTheField_Binds_AndIsNotWrittenBack()
    {
        var dto = JsonSerializer.Deserialize<CategoryDto>(
            """{"Name":"Weapons","IconMaterialRefId":0,"IconMaterialRefNamespaceKey":"minecraft:iron_sword"}""", ApiJson())!;

        Assert.Equal("minecraft:iron_sword", dto.IconNamespaceKey);
        Assert.DoesNotContain("iconMaterialRefNamespaceKey", JsonSerializer.Serialize(dto, ApiJson()));
    }

    [Fact]
    public void ItemBlueprint_ExistingIconNamespaceKey_StillBinds()
    {
        var dto = JsonSerializer.Deserialize<ItemBlueprintCreateDto>(
            """{"Name":"Steel Sword","iconNamespaceKey":"minecraft:iron_sword"}""", ApiJson())!;

        Assert.Equal("minecraft:iron_sword", dto.IconNamespaceKey);
    }

    // ===== Bug 5: hybrid enchantment picker key =====

    [Fact]
    public void EnchantmentDefinition_HybridEnchantmentKeyNamedAfterTheField_Binds()
    {
        const string body = """{"Key":"minecraft:sharpness","DisplayName":"Sharpness","MinecraftEnchantmentRefNamespaceKey":"minecraft:sharpness"}""";

        Assert.Equal("minecraft:sharpness",
            JsonSerializer.Deserialize<EnchantmentDefinitionCreateDto>(body, ApiJson())!.EnchantmentNamespaceKey);
        Assert.Equal("minecraft:sharpness",
            JsonSerializer.Deserialize<EnchantmentDefinitionUpdateDto>(body, ApiJson())!.EnchantmentNamespaceKey);
    }

    // ===== Bug 6: abilityDefinition absent vs. null =====

    [Fact]
    public void EnchantmentDefinitionUpdate_TellsAbsentAbilityFromExplicitNull()
    {
        var absent = JsonSerializer.Deserialize<EnchantmentDefinitionUpdateDto>(
            """{"Id":3,"Key":"knk:frost","DisplayName":"Frost","IsCustom":true}""", ApiJson())!;
        var cleared = JsonSerializer.Deserialize<EnchantmentDefinitionUpdateDto>(
            """{"Id":3,"Key":"knk:frost","DisplayName":"Frost","IsCustom":true,"abilityDefinition":null}""", ApiJson())!;

        Assert.False(absent.HasAbilityDefinition);
        Assert.True(cleared.HasAbilityDefinition);
        Assert.Null(cleared.AbilityDefinition);
    }
}
