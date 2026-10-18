using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Migrations;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Repositories;

/// <summary>
/// KNG-164: MinSelection, MaxSelection, CanCreate and IncrementValue were on FormFieldDto only, so a
/// value set in the form builder was dropped on save. They are FormField columns now (nullable, null =
/// no limit / creatable / step 1), mapped both ways and kept by create and update.
/// </summary>
public class FormFieldSelectionLimitsTests
{
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<FormMappingProfile>()).CreateMapper();
    private readonly DbContextOptions<KnKDbContext> _options = new DbContextOptionsBuilder<KnKDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;

    private static FormFieldDto DistrictsField(int? min, int? max, bool? canCreate, int? increment) => new()
    {
        FieldName = "DistrictIds",
        Label = "Districts",
        FieldType = FieldType.List,
        ObjectType = "District",
        IsRequired = true,
        MinSelection = min,
        MaxSelection = max,
        CanCreate = canCreate,
        IncrementValue = increment
    };

    [Fact]
    public void Mapping_KeepsTheLimitsBothWays()
    {
        var model = _mapper.Map<FormField>(DistrictsField(1, 3, false, 5));

        Assert.Equal((1, 3, false, 5), (model.MinSelection, model.MaxSelection, model.CanCreate, model.IncrementValue));
        var dto = _mapper.Map<FormFieldDto>(model);
        Assert.Equal((1, 3, false, 5), (dto.MinSelection, dto.MaxSelection, dto.CanCreate, dto.IncrementValue));
    }

    [Fact]
    public void Mapping_LeavesUnsetLimitsNull()
    {
        var dto = _mapper.Map<FormFieldDto>(_mapper.Map<FormField>(DistrictsField(null, null, null, null)));

        Assert.Null(dto.MinSelection);
        Assert.Null(dto.MaxSelection);
        Assert.Null(dto.CanCreate);
        Assert.Null(dto.IncrementValue);
    }

    [Fact]
    public async Task CreateAndUpdate_StoreTheLimits()
    {
        var config = new FormConfiguration
        {
            Name = "Street - Default",
            EntityTypeName = "Street",
            Steps = { new FormStep { StepName = "Basics", Fields = { _mapper.Map<FormField>(DistrictsField(1, null, null, null)) } } }
        };
        await using (var db = new KnKDbContext(_options))
        {
            await new FormConfigurationRepository(db).AddAsync(config);
        }

        await using (var db = new KnKDbContext(_options))
        {
            var stored = await db.FormFields.SingleAsync();
            Assert.Equal(1, stored.MinSelection);
            Assert.Null(stored.MaxSelection);
            Assert.Null(stored.CanCreate);
        }

        // The builder's save sends the whole graph with ids; UpdateAsync merges field by field.
        var stepId = config.Steps[0].Id;
        var fieldId = config.Steps[0].Fields[0].Id;
        var edited = _mapper.Map<FormField>(DistrictsField(2, 4, false, 10));
        edited.Id = fieldId;
        edited.FieldGuid = config.Steps[0].Fields[0].FieldGuid;
        var update = new FormConfiguration
        {
            Id = config.Id,
            Name = config.Name,
            EntityTypeName = config.EntityTypeName,
            Steps = { new FormStep { Id = stepId, StepGuid = config.Steps[0].StepGuid, StepName = "Basics", Fields = { edited } } }
        };
        await using (var db = new KnKDbContext(_options))
        {
            await new FormConfigurationRepository(db).UpdateAsync(update);
        }

        await using (var db = new KnKDbContext(_options))
        {
            var stored = await db.FormFields.SingleAsync();
            Assert.Equal((2, 4, false, 10), (stored.MinSelection, stored.MaxSelection, stored.CanCreate, stored.IncrementValue));
        }
    }

    [Fact]
    public void Migration_AddsFourNullableColumnsAndNothingElse()
    {
        var up = new AddFormFieldSelectionLimits().UpOperations;

        var columns = up.OfType<AddColumnOperation>().ToList();
        Assert.Equal(up.Count, columns.Count);
        Assert.All(columns, c => Assert.Equal("FormFields", c.Table));
        Assert.All(columns, c => Assert.True(c.IsNullable));
        Assert.Equal(new[] { "CanCreate", "IncrementValue", "MaxSelection", "MinSelection" }, columns.Select(c => c.Name).OrderBy(n => n));
    }
}
