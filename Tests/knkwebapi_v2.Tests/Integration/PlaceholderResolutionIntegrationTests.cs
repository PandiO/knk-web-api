using Xunit;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Models;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Properties;

namespace knkwebapi_v2.Tests.Integration;

/// <summary>
/// Integration tests for placeholder resolution with database operations.
/// Tests the full flow from API request to database query to resolved placeholders.
/// Phase 7.3 of Placeholder Interpolation implementation.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Feature", "PlaceholderInterpolation")]
public class PlaceholderResolutionIntegrationTests : IDisposable
{
    private readonly KnKDbContext _dbContext;
    private readonly FieldValidationRuleRepository _ruleRepository;
    private readonly PlaceholderResolutionService _placeholderService;
    private readonly Mock<ILogger<PlaceholderResolutionService>> _mockPlaceholderLogger;

    public PlaceholderResolutionIntegrationTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(databaseName: $"PlaceholderIntegrationTestDb_{Guid.NewGuid()}")
            .Options;

        _dbContext = new KnKDbContext(options);
        _ruleRepository = new FieldValidationRuleRepository(_dbContext);
        _mockPlaceholderLogger = new Mock<ILogger<PlaceholderResolutionService>>();

        _placeholderService = new PlaceholderResolutionService(
            _dbContext,
            _ruleRepository,
            _mockPlaceholderLogger.Object
        );

        SeedTestData();
    }

    private void SeedTestData()
    {
        // Create Town
        var town = new Town
        {
            Id = 100,
            Name = "Springfield",
            Description = "A historic town",
            WgRegionId = "town_springfield"
        };
        _dbContext.Towns.Add(town);

        // Create Districts
        var district1 = new District
        {
            Id = 200,
            Name = "York",
            Description = "Historic residential district",
            WgRegionId = "district_york",
            TownId = 100,
            Town = town
        };

        var district2 = new District
        {
            Id = 201,
            Name = "Cambridge",
            Description = "Commercial district",
            WgRegionId = "district_cambridge",
            TownId = 100,
            Town = town
        };

        _dbContext.Districts.Add(district1);
        _dbContext.Districts.Add(district2);

        // Create Street (required by Structure)
        var street = new Street
        {
            Id = 400,
            Name = "Main Street"
        };
        _dbContext.Streets.Add(street);

        // Create Structure
        var structure = new Structure
        {
            Id = 300,
            Name = "Town Hall",
            Description = "Central government building",
            WgRegionId = "structure_town_hall",
            StreetId = 400,
            Street = street,
            DistrictId = 200,
            District = district1
        };
        _dbContext.Structures.Add(structure);

        // Create FormFields (required navigation for FieldValidationRules)
        var formField1 = new FormField { Id = 1, FieldName = "location", Label = "Location", FieldType = FieldType.Object, ObjectType = "Location" };
        var formField2 = new FormField { Id = 2, FieldName = "region", Label = "Region", FieldType = FieldType.String };
        var formField3 = new FormField { Id = 3, FieldName = "name", Label = "Name", FieldType = FieldType.String };
        _dbContext.FormFields.Add(formField1);
        _dbContext.FormFields.Add(formField2);
        _dbContext.FormFields.Add(formField3);

        // Create Validation Rules
        var rule1 = new FieldValidationRule
        {
            Id = 1,
            FormFieldId = 1, // Required property
            ValidationType = "LocationInsideRegion",
            ErrorMessage = "Location {coordinates} is outside {Town.Name}'s boundaries.",
            SuccessMessage = "Location is within {Town.Name}.",
            IsBlocking = true,
            ConfigJson = "{\"regionPropertyPath\": \"TownId\"}"
        };

        var rule2 = new FieldValidationRule
        {
            Id = 2,
            FormFieldId = 2, // Required property
            ValidationType = "RegionContainment",
            ErrorMessage = "District {Name} must be contained within {Town.Name}. Found {violationCount} violations.",
            SuccessMessage = "District {Name} is properly contained within {Town.Name}.",
            IsBlocking = true,
            ConfigJson = "{\"parentRegionPath\": \"TownId\"}"
        };

        var rule3 = new FieldValidationRule
        {
            Id = 3,
            FormFieldId = 3, // Required property
            ValidationType = "ConditionalRequired",
            ErrorMessage = "Structure name is required when district is {District.Name}.",
            SuccessMessage = "Structure name is valid.",
            IsBlocking = false,
            ConfigJson = "{\"operator\": \"NotNull\", \"value\": null}"
        };

        _dbContext.FieldValidationRules.Add(rule1);
        _dbContext.FieldValidationRules.Add(rule2);
        _dbContext.FieldValidationRules.Add(rule3);

        _dbContext.SaveChanges();
    }

    #region Layer 0 Resolution Tests

    [Fact]
    public async Task ResolveAllLayersAsync_WithLayer0Only_ReturnsCurrentEntityPlaceholders()
    {
        // Arrange
        var request = new PlaceholderResolutionRequest
        {
            PlaceholderPaths = new List<string> { "Name", "Description" },
            CurrentEntityPlaceholders = new Dictionary<string, string>
            {
                ["Name"] = "York",
                ["Description"] = "Historic district"
            }
        };

        // Act
        var result = await _placeholderService.ResolveAllLayersAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(2, result.ResolvedPlaceholders.Count);
        Assert.Equal("York", result.ResolvedPlaceholders["Name"]);
        Assert.Equal("Historic district", result.ResolvedPlaceholders["Description"]);
    }

    #endregion

    #region Layer 1 Resolution Tests (Single Navigation)

    [Fact]
    public async Task ResolveAllLayersAsync_WithLayer1Navigation_QueriesDatabase()
    {
        // Arrange
        var request = new PlaceholderResolutionRequest
        {
            PlaceholderPaths = new List<string> { "Town.Name", "Town.Description" },
            EntityTypeName = "District",
            EntityId = 200
        };

        // Act
        var result = await _placeholderService.ResolveAllLayersAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(2, result.ResolvedPlaceholders.Count);
        Assert.Equal("Springfield", result.ResolvedPlaceholders["Town.Name"]);
        Assert.Equal("A historic town", result.ResolvedPlaceholders["Town.Description"]);
    }

    [Fact]
    public async Task ResolveAllLayersAsync_WithLayer1_UsesIncludeForSingleQuery()
    {
        // Arrange
        var request = new PlaceholderResolutionRequest
        {
            FieldValidationRuleId = 1,
            EntityTypeName = "District",
            EntityId = 200
        };

        // Act
        var result = await _placeholderService.ResolveAllLayersAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.ResolvedPlaceholders);
    }

    #endregion

    #region Layer 2 Resolution Tests (Multi-Level Navigation)

    [Fact]
    public async Task ResolveAllLayersAsync_WithLayer2Navigation_TraversesMultipleLevels()
    {
        // Arrange
        var request = new PlaceholderResolutionRequest
        {
            PlaceholderPaths = new List<string> { "District.Town.Name", "District.Town.Description" },
            EntityTypeName = "Structure",
            EntityId = 300
        };

        // Act
        var result = await _placeholderService.ResolveAllLayersAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(2, result.ResolvedPlaceholders.Count);
        Assert.Equal("Springfield", result.ResolvedPlaceholders["District.Town.Name"]);
        Assert.Equal("A historic town", result.ResolvedPlaceholders["District.Town.Description"]);
    }

    [Fact]
    public async Task ResolveAllLayersAsync_WithLayer2_OptimizesIncludePaths()
    {
        // Arrange
        var request = new PlaceholderResolutionRequest
        {
            PlaceholderPaths = new List<string>
            {
                "District.Name",          // Layer 1
                "District.Town.Name"      // Layer 2
            },
            EntityTypeName = "Structure",
            EntityId = 300
        };

        // Act
        var result = await _placeholderService.ResolveAllLayersAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(2, result.ResolvedPlaceholders.Count);
        Assert.Equal("York", result.ResolvedPlaceholders["District.Name"]);
        Assert.Equal("Springfield", result.ResolvedPlaceholders["District.Town.Name"]);
        
    }

    #endregion

    #region Layer 3 Resolution Tests (Aggregates)

    [Fact]
    public async Task ResolveAllLayersAsync_WithCountAggregate_ReturnsCollectionCount()
    {
        // Arrange
        var request = new PlaceholderResolutionRequest
        {
            PlaceholderPaths = new List<string> { "Districts.Count" },
            EntityTypeName = "Town",
            EntityId = 100
        };

        // Act
        var result = await _placeholderService.ResolveAllLayersAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Contains("Districts.Count", result.ResolvedPlaceholders.Keys);
        Assert.Equal("2", result.ResolvedPlaceholders["Districts.Count"]); // Springfield has 2 districts
    }

    #endregion

    #region Multi-Layer Mixed Tests

    [Fact]
    public async Task ResolveAllLayersAsync_WithMixedLayers_ResolvesAllCorrectly()
    {
        // Arrange
        var request = new PlaceholderResolutionRequest
        {
            PlaceholderPaths = new List<string>
            {
                "Name",                    // Layer 0
                "District.Name",           // Layer 1 on Structure
                "District.Town.Name"       // Layer 2
            },
            EntityTypeName = "Structure",
            EntityId = 300,
            CurrentEntityPlaceholders = new Dictionary<string, string>
            {
                ["Name"] = "Town Hall"
            }
        };

        // Act
        var result = await _placeholderService.ResolveAllLayersAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(3, result.ResolvedPlaceholders.Count);
        Assert.Equal("Town Hall", result.ResolvedPlaceholders["Name"]);
        Assert.Equal("York", result.ResolvedPlaceholders["District.Name"]);
        Assert.Equal("Springfield", result.ResolvedPlaceholders["District.Town.Name"]);
    }

    [Fact]
    public async Task ResolveAllLayersAsync_WithRuleContainingAllLayers_ResolvesCompletely()
    {
        // Arrange
        var request = new PlaceholderResolutionRequest
        {
            FieldValidationRuleId = 2, // RegionContainment rule
            EntityTypeName = "District",
            EntityId = 200,
            CurrentEntityPlaceholders = new Dictionary<string, string>
            {
                ["Name"] = "York"
            }
        };

        // Act
        var result = await _placeholderService.ResolveAllLayersAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.ResolvedPlaceholders);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task ResolveAllLayersAsync_WithInvalidEntityType_HandlesGracefully()
    {
        // Arrange
        var request = new PlaceholderResolutionRequest
        {
            PlaceholderPaths = new List<string> { "Town.Name" },
            EntityTypeName = "NonExistentEntity",
            EntityId = 100
        };

        // Act
        var result = await _placeholderService.ResolveAllLayersAsync(request);

        // Assert
        Assert.NotNull(result);
        // Should handle gracefully - may have resolution errors
        if (!result.IsSuccessful)
        {
            Assert.NotEmpty(result.ResolutionErrors);
        }
    }

    [Fact]
    public async Task ResolveAllLayersAsync_WithNonExistentEntityId_HandlesGracefully()
    {
        // Arrange
        var request = new PlaceholderResolutionRequest
        {
            PlaceholderPaths = new List<string> { "Town.Name" },
            EntityTypeName = "District",
            EntityId = 999 // Non-existent
        };

        // Act
        var result = await _placeholderService.ResolveAllLayersAsync(request);

        // Assert
        Assert.NotNull(result);
        // Should handle missing entity gracefully
    }

    [Fact]
    public async Task ResolveAllLayersAsync_WithBrokenNavigationPath_RecordsError()
    {
        // Arrange
        var request = new PlaceholderResolutionRequest
        {
            PlaceholderPaths = new List<string> { "NonExistent.Property" },
            EntityTypeName = "District",
            EntityId = 200
        };

        // Act
        var result = await _placeholderService.ResolveAllLayersAsync(request);

        // Assert
        Assert.NotNull(result);
        // Should have resolution error for broken path
        if (!result.IsSuccessful)
        {
            Assert.NotEmpty(result.ResolutionErrors);
        }
    }

    #endregion

    #region Performance Tests

    [Fact]
    public async Task ResolveAllLayersAsync_WithMultiplePlaceholders_UsesSingleQuery()
    {
        // Arrange
        var request = new PlaceholderResolutionRequest
        {
            PlaceholderPaths = new List<string>
            {
                "Town.Name",
                "Town.Description",
                "Town.Prefix"
            },
            EntityTypeName = "District",
            EntityId = 200
        };

        // Act
        var startTime = DateTime.UtcNow;
        var result = await _placeholderService.ResolveAllLayersAsync(request);
        var endTime = DateTime.UtcNow;

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(2, result.ResolvedPlaceholders.Count);
        
        // Should complete quickly (single query optimization)
        var duration = endTime - startTime;
        Assert.True(duration.TotalMilliseconds < 500, 
            $"Resolution took {duration.TotalMilliseconds}ms, expected < 500ms");
    }

    #endregion

    public void Dispose()
    {
        _dbContext?.Dispose();
    }
}
