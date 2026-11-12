using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// PandiO/knk-workspace#71 on a real MySQL: the migration AddConfigurationDrafts keeps every existing configuration
/// published, and a brand-new builder draft (new steps / sections) publishes in one transaction, moving the default.
/// </summary>
[Trait("Category", "requires-mysql")]
public class ConfigurationDraftsMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private const string Previous = "20261108181026_SeedDashboardActionNodes";
    private const string Migration = "20261109101501_AddConfigurationDrafts";
    private readonly MySqlTestDatabase _db;

    public ConfigurationDraftsMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddDbContext<KnKDbContext>(o => o.UseMySql(_db.ConnectionString, ServerVersion.AutoDetect(_db.ConnectionString)));
        services.AddSingleton<IMapper>(new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<FormMappingProfile>();
            cfg.AddProfile<DisplayMappingProfile>();
        }).CreateMapper());
        return services.BuildServiceProvider();
    }

    [MySqlFact]
    public async Task Migration_KeepsExistingConfigurations_Published()
    {
        var name = "Legacy " + Guid.NewGuid().ToString("N")[..8];
        await using (var ctx = _db.NewContext())
        {
            var migrator = ctx.GetService<IMigrator>();
            await migrator.MigrateAsync(Previous);
            await ctx.Database.ExecuteSqlRawAsync(
                "INSERT INTO FormConfigurations (ConfigurationGuid, Name, EntityTypeName, IsDefault, CreatedAt, StepOrderJson) " +
                "VALUES (UUID(), {0}, 'Category', 0, UTC_TIMESTAMP(), '[]')", name);
            await migrator.MigrateAsync(Migration);
        }

        await using (var ctx = _db.NewContext())
        {
            var row = await ctx.FormConfigurations.AsNoTracking().SingleAsync(c => c.Name == name);
            Assert.False(row.IsDraft);
            Assert.Null(row.DraftJson);
            Assert.Null(row.PublishedAt);

            // Down and up again: the columns go and come back, the row stays.
            var migrator = ctx.GetService<IMigrator>();
            await migrator.MigrateAsync(Previous);
            await migrator.MigrateAsync();
            Assert.False((await ctx.FormConfigurations.AsNoTracking().SingleAsync(c => c.Name == name)).IsDraft);
        }
    }

    [MySqlFact]
    public async Task NewFormDraft_PublishesAsTheDefault()
    {
        await using var provider = Services();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;
        var ctx = sp.GetRequiredService<KnKDbContext>();
        var mapper = sp.GetRequiredService<IMapper>();
        var repo = new FormConfigurationRepository(ctx);
        var metadata = new MetadataService(sp.GetRequiredService<IServiceScopeFactory>());
        var validation = new FormTemplateValidationService();
        var configs = new FormConfigurationService(repo, mapper, validation, metadata);
        var portability = new FormConfigurationPortabilityService(ctx, repo, validation, metadata, new FormConfigurationSeedOptions { Directory = Path.GetTempPath() });
        var drafts = new FormConfigurationDraftService(ctx, repo, configs, portability, mapper);

        var entity = "Tag";
        var old = await configs.CreateAsync(new FormConfigurationDto
        {
            EntityTypeName = entity, ConfigurationName = "Old", IsDefault = true,
            Steps = { new FormStepDto { StepName = "General", Fields = { new FormFieldDto { FieldName = "Name", Label = "Name", FieldType = FieldType.String, IsRequired = true } } } }
        });
        var state = await drafts.CreateDraftAsync(new FormConfigurationDto
        {
            EntityTypeName = entity, ConfigurationName = "New", IsDefault = true,
            Steps =
            {
                new FormStepDto { Id = "temp-1", StepName = "One", Order = 0, Fields = { new FormFieldDto { Id = "temp-2", FieldName = "Name", Label = "Name", FieldType = FieldType.String, IsRequired = true } } },
                new FormStepDto { Id = "temp-3", StepName = "Two", Order = 1 }
            }
        });

        var published = await drafts.PublishAsync(int.Parse(state.Configuration.Id!));

        Assert.Equal(new[] { "One", "Two" }, published.Steps.Select(s => s.StepName));
        Assert.True(published.IsDefault);
        Assert.False((await configs.GetByIdAsync(int.Parse(old.Id!)))!.IsDefault);
        Assert.Equal("New", (await configs.GetDefaultByEntityTypeNameAsync(entity)).ConfigurationName);
    }

    [MySqlFact]
    public async Task NewDisplayDraft_WithNewSections_Publishes()
    {
        await using var provider = Services();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;
        var ctx = sp.GetRequiredService<KnKDbContext>();
        var mapper = sp.GetRequiredService<IMapper>();
        var repo = new DisplayConfigurationRepository(ctx);
        var configs = new DisplayConfigurationService(repo, mapper);
        var portability = new DisplayConfigurationPortabilityService(ctx, new MetadataService(sp.GetRequiredService<IServiceScopeFactory>()),
            new DisplayConfigurationSeedOptions { Directory = Path.GetTempPath() });
        var drafts = new DisplayConfigurationDraftService(ctx, repo, configs, portability, mapper);

        var state = await drafts.CreateDraftAsync(new DisplayConfigurationDto
        {
            EntityTypeName = "Grade", Name = "Grade page", IsDefault = true,
            Sections =
            {
                new DisplaySectionDto
                {
                    Id = "temp-1", SectionGuid = Guid.NewGuid().ToString(), SectionName = "Overview",
                    Fields = { new DisplayFieldDto { Id = "temp-2", FieldGuid = Guid.NewGuid().ToString(), FieldName = "Name", Label = "Name", FieldType = "String" } }
                }
            }
        });

        var published = await drafts.PublishAsync(int.Parse(state.Configuration.Id!));

        Assert.False(published.IsDraft);
        Assert.True(published.IsDefault);
        Assert.Equal("Name", published.Sections.Single().Fields.Single().FieldName);
        Assert.Equal(published.Id, (await configs.GetDefaultByEntityTypeNameAsync("Grade"))!.Id);
    }
}
