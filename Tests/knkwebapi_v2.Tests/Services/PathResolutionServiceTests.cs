using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services
{
    /// <summary>
    /// Unit tests for PathResolutionService.
    /// Tests cover v1 scope (single-hop paths only) and validation logic.
    /// 
    /// TEST COVERAGE TARGET: 80%+
    /// </summary>
    public class PathResolutionServiceTests
    {
        private readonly Mock<ILogger<PathResolutionService>> _mockLogger;
        private readonly Mock<IMetadataService> _mockMetadataService;
        private readonly PathResolutionService _service;

        public PathResolutionServiceTests()
        {
            _mockLogger = new Mock<ILogger<PathResolutionService>>();
            _mockMetadataService = new Mock<IMetadataService>();
            _service = new PathResolutionService(_mockLogger.Object, _mockMetadataService.Object);

            // Setup default metadata service behavior
            _mockMetadataService
                .Setup(m => m.GetEntityMetadata(It.IsAny<string>()))
                .Returns((string entityName) =>
                {
                    // Return non-null metadata for known entities
                    if (entityName == "Town" || entityName == "District" || entityName == "Structure")
                    {
                        return new EntityMetadataDto(); // Mock metadata object
                    }
                    return null;
                });
        }

        #region ResolvePathAsync Tests

        [Fact]
        public async Task ResolvePathAsync_WithNullValue_ReturnsNull()
        {
            // Arrange
            object? nullValue = null;

            // Act
            var result = await _service.ResolvePathAsync("Town", "wgRegionId", nullValue);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task ResolvePathAsync_WithEmptyPath_ReturnsCurrentValue()
        {
            // Arrange
            var town = new Town { Id = 1, Name = "Springfield", WgRegionId = "town_1" };

            // Act
            var result = await _service.ResolvePathAsync("Town", "", town);

            // Assert
            result.Should().BeSameAs(town);
        }

        [Fact]
        public async Task ResolvePathAsync_WithSingleProperty_ReturnsPropertyValue()
        {
            // Arrange
            var town = new Town { Id = 1, Name = "Springfield", WgRegionId = "town_1" };

            // Act
            var result = await _service.ResolvePathAsync("Town", "WgRegionId", town);

            // Assert
            result.Should().Be("town_1");
        }

        [Fact]
        public async Task ResolvePathAsync_WithSingleHopPath_ReturnsNestedPropertyValue()
        {
            // Arrange
            var town = new Town { Id = 1, Name = "Springfield", WgRegionId = "town_1" };
            var district = new District
            {
                Id = 1,
                Name = "Downtown",
                TownId = 1,
                Town = town
            };

            // Act
            var result = await _service.ResolvePathAsync("District", "Town.WgRegionId", district);

            // Assert
            result.Should().Be("town_1");
        }

        [Fact]
        public async Task ResolvePathAsync_WithDictionary_ResolvesCaseInsensitively()
        {
            // Arrange
            var formContext = new Dictionary<string, object?>
            {
                { "id", 1 },
                { "name", "Springfield" },
                { "wgRegionId", "town_1" }
            };

            // Act
            var result = await _service.ResolvePathAsync("Town", "WgRegionId", formContext);

            // Assert
            result.Should().Be("town_1");
        }

        [Fact]
        public async Task ResolvePathAsync_WithMultiHopPath_ReturnsNull_V1Constraint()
        {
            // Arrange
            var town = new Town { Id = 1, WgRegionId = "town_1" };
            var district = new District { Id = 1, Town = town };
            var structure = new Structure { Id = 1, District = district };

            // Act - Multi-hop path should fail in v1
            var result = await _service.ResolvePathAsync("Structure", "District.Town.WgRegionId", structure);

            // Assert
            result.Should().BeNull("v1 only supports single-hop paths");
        }

        [Fact]
        public async Task ResolvePathAsync_WithNonExistentProperty_ReturnsNull()
        {
            // Arrange
            var town = new Town { Id = 1, Name = "Springfield" };

            // Act
            var result = await _service.ResolvePathAsync("Town", "InvalidProperty", town);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task ResolvePathAsync_WithNullIntermediateValue_ReturnsNull()
        {
            // Arrange
            var district = new District { Id = 1, Name = "Downtown", Town = null };

            // Act
            var result = await _service.ResolvePathAsync("District", "Town.WgRegionId", district);

            // Assert
            result.Should().BeNull("navigation property is null");
        }

        #endregion

        #region ValidatePathAsync Tests

        [Fact]
        public async Task ValidatePathAsync_WithEmptyPath_ReturnsInvalid()
        {
            // Act
            var result = await _service.ValidatePathAsync("Town", "");

            // Assert
            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("cannot be empty");
        }

        [Fact]
        public async Task ValidatePathAsync_WithLeadingDot_ReturnsInvalid()
        {
            // Act
            var result = await _service.ValidatePathAsync("Town", ".wgRegionId");

            // Assert
            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("invalid syntax");
        }

        [Fact]
        public async Task ValidatePathAsync_WithTrailingDot_ReturnsInvalid()
        {
            // Act
            var result = await _service.ValidatePathAsync("Town", "wgRegionId.");

            // Assert
            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("invalid syntax");
        }

        [Fact]
        public async Task ValidatePathAsync_WithConsecutiveDots_ReturnsInvalid()
        {
            // Act
            var result = await _service.ValidatePathAsync("Town", "Name..WgRegionId");

            // Assert
            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("invalid syntax");
        }

        [Fact]
        public async Task ValidatePathAsync_WithSpaces_ReturnsInvalid()
        {
            // Act
            var result = await _service.ValidatePathAsync("Town", "Name . WgRegionId");

            // Assert
            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("invalid syntax");
        }

        [Fact]
        public async Task ValidatePathAsync_WithMultiHopPath_ReturnsInvalid_V1Constraint()
        {
            // Act
            var result = await _service.ValidatePathAsync("Structure", "District.Town.WgRegionId");

            // Assert
            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("multiple levels");
            result.ErrorMessage.Should().Contain("v1 only supports single-hop");
            result.Suggestion.Should().Contain("v2");
        }

        [Fact]
        public async Task ValidatePathAsync_WithBrackets_ReturnsInvalidCollectionNavigation()
        {
            // Act
            var result = await _service.ValidatePathAsync("Town", "Districts[0].Name");

            // Assert
            result.IsValid.Should().BeFalse();
            result.IsCollectionNavigation.Should().BeTrue();
            result.ErrorMessage.Should().Contain("collection navigation");
            result.Suggestion.Should().Contain("v2");
        }

        [Fact]
        public async Task ValidatePathAsync_WithNonExistentEntity_ReturnsInvalid()
        {
            // Arrange
            _mockMetadataService
                .Setup(m => m.GetEntityMetadata("InvalidEntity"))
                .Returns((EntityMetadataDto?)null);

            // Act
            var result = await _service.ValidatePathAsync("InvalidEntity", "SomeProperty");

            // Assert
            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Entity 'InvalidEntity' not found");
        }

        [Fact]
        public async Task ValidatePathAsync_WithValidSingleProperty_ReturnsValid()
        {
            // Act
            var result = await _service.ValidatePathAsync("Town", "Name");

            // Assert
            result.IsValid.Should().BeTrue();
            result.ErrorMessage.Should().BeNull();
        }

        [Fact]
        public async Task ValidatePathAsync_WithValidSingleHopPath_ReturnsValid()
        {
            // Act
            var result = await _service.ValidatePathAsync("Town", "WgRegionId");

            // Assert
            result.IsValid.Should().BeTrue();
            result.ErrorMessage.Should().BeNull();
        }

        [Fact]
        public async Task ValidatePathAsync_WithNonExistentProperty_ReturnsInvalidWithSuggestions()
        {
            // Act
            var result = await _service.ValidatePathAsync("Town", "InvalidProperty");

            // Assert
            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Property 'InvalidProperty' not found");
            result.MissingProperties.Should().Contain("InvalidProperty");
            result.Suggestion.Should().Contain("Available properties");
        }

        #endregion

        #region GetIncludePathsForNavigation Tests

        [Fact]
        public void GetIncludePathsForNavigation_WithEmptyPath_ReturnsEmptyArray()
        {
            // Act
            var result = _service.GetIncludePathsForNavigation("");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void GetIncludePathsForNavigation_WithSingleProperty_ReturnsEmptyArray()
        {
            // Act
            var result = _service.GetIncludePathsForNavigation("wgRegionId");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void GetIncludePathsForNavigation_WithSingleHopPath_ReturnsEntityPath()
        {
            // Act
            var result = _service.GetIncludePathsForNavigation("Town.WgRegionId");

            // Assert
            result.Should().HaveCount(1);
            result[0].Should().Be("Town");
        }

        [Fact]
        public void GetIncludePathsForNavigation_WithMultiHopPath_ReturnsAllIntermediatePaths()
        {
            // Act (testing v2 behavior even though v1 doesn't support it)
            var result = _service.GetIncludePathsForNavigation("District.Town.WgRegionId");

            // Assert
            result.Should().HaveCount(2);
            result[0].Should().Be("District");
            result[1].Should().Be("District.Town");
        }

        #endregion

        #region GetEntityPropertiesAsync Tests

        [Fact]
        public async Task GetEntityPropertiesAsync_WithValidEntity_ReturnsAllProperties()
        {
            // Act
            var result = await _service.GetEntityPropertiesAsync("Town");

            // Assert
            result.Should().NotBeEmpty();
            result.Should().Contain(p => p.PropertyName == "Id");
            result.Should().Contain(p => p.PropertyName == "Name");
            result.Should().Contain(p => p.PropertyName == "WgRegionId");
        }

        [Fact]
        public async Task GetEntityPropertiesAsync_DistinguishesNavigationProperties()
        {
            // Act
            var result = await _service.GetEntityPropertiesAsync("District");

            // Assert
            var townProperty = result.FirstOrDefault(p => p.PropertyName == "Town");
            townProperty.Should().NotBeNull();
            townProperty!.IsNavigationProperty.Should().BeTrue();
            townProperty.PropertyType.Should().Be("Town");
        }

        [Fact]
        public async Task GetEntityPropertiesAsync_DetectsCollectionProperties()
        {
            // Act
            var result = await _service.GetEntityPropertiesAsync("Town");

            // Assert
            var districtsProperty = result.FirstOrDefault(p => p.PropertyName == "Districts");
            if (districtsProperty != null) // Only if Town has Districts collection
            {
                districtsProperty.IsCollection.Should().BeTrue();
            }
        }

        [Fact]
        public async Task GetEntityPropertiesAsync_WithInvalidEntity_ReturnsEmptyList()
        {
            // Act
            var result = await _service.GetEntityPropertiesAsync("NonExistentEntity");

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task GetEntityPropertiesAsync_IncludesPrimitiveTypes()
        {
            // Act
            var result = await _service.GetEntityPropertiesAsync("Town");

            // Assert
            var idProperty = result.FirstOrDefault(p => p.PropertyName == "Id");
            idProperty.Should().NotBeNull();
            idProperty!.PropertyType.Should().Be("int");
            idProperty.IsNavigationProperty.Should().BeFalse();

            var nameProperty = result.FirstOrDefault(p => p.PropertyName == "Name");
            nameProperty.Should().NotBeNull();
            nameProperty!.PropertyType.Should().Be("string");
        }

        #endregion

        #region Edge Cases and Error Handling

        [Fact]
        public async Task ResolvePathAsync_WithCaseInsensitivePropertyName_Succeeds()
        {
            // Arrange
            var town = new Town { Id = 1, WgRegionId = "town_1" };

            // Act - Different casing for property name
            var result = await _service.ResolvePathAsync("Town", "wgregionid", town);

            // Assert
            result.Should().Be("town_1");
        }

        [Fact]
        public async Task ValidatePathAsync_IsCaseInsensitiveForPropertyNames()
        {
            // Act
            var result = await _service.ValidatePathAsync("Town", "wgregionid");

            // Assert
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task ResolvePathAsync_WithComplexNestedDictionary_ResolvesCorrectly()
        {
            // Arrange
            var formContext = new Dictionary<string, object?>
            {
                { "Town", new Dictionary<string, object?>
                    {
                        { "id", 1 },
                        { "name", "Springfield" },
                        { "wgRegionId", "town_1" }
                    }
                }
            };

            // Act
            var result = await _service.ResolvePathAsync("District", "Town.wgRegionId", formContext);

            // Assert
            result.Should().Be("town_1");
        }

        #endregion

        #region V1 Constraints Validation

        [Theory]
        [InlineData("Entity.Relation.Property")]
        [InlineData("A.B.C.D")]
        [InlineData("District.Town.WgRegionId")]
        public async Task ValidatePathAsync_RejectsMultiHopPaths_V1Constraint(string path)
        {
            // Act
            var result = await _service.ValidatePathAsync("Structure", path);

            // Assert
            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("v1 only supports single-hop");
        }

        [Theory]
        [InlineData("Collection[0]")]
        [InlineData("Items[first]")]
        [InlineData("Districts[all].Name")]
        public async Task ValidatePathAsync_RejectsCollectionOperators_V1Constraint(string path)
        {
            // Act
            var result = await _service.ValidatePathAsync("Town", path);

            // Assert
            result.IsValid.Should().BeFalse();
            result.IsCollectionNavigation.Should().BeTrue();
            result.ErrorMessage.Should().Contain("collection navigation");
        }

        // A path is relative to the entity it is validated for: DependencyResolutionService passes the form's
        // EntityTypeName, so "Town.WgRegionId" is a District form's path (District.Town.WgRegionId, the
        // dependency-resolution design's "Create District" example). PandiO/knk-workspace#99: the two
        // single-hop rows used to run against "Town", which has no Town property.
        [Theory]
        [InlineData("Town", "Name")]
        [InlineData("Town", "WgRegionId")]
        [InlineData("District", "Town.Name")]
        [InlineData("District", "Town.WgRegionId")]
        public async Task ValidatePathAsync_AllowsValidV1Paths(string entityTypeName, string path)
        {
            // Act
            var result = await _service.ValidatePathAsync(entityTypeName, path);

            // Assert
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task ValidatePathAsync_PathIsRelativeToTheEntity_TownHasNoTownProperty()
        {
            var result = await _service.ValidatePathAsync("Town", "Town.Name");

            result.IsValid.Should().BeFalse();
            result.MissingProperties.Should().Contain("Town");
        }

        #endregion
    }
}
