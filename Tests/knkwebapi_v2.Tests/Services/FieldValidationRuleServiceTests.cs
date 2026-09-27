using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services
{
    /// <summary>
    /// Unit tests for FieldValidationRuleService CRUD and health check operations.
    /// </summary>
    public class FieldValidationRuleServiceTests
    {
        private readonly Mock<IFieldValidationRuleRepository> _mockRuleRepository;
        private readonly Mock<IFormFieldRepository> _mockFieldRepository;
        private readonly Mock<IFormConfigurationRepository> _mockConfigRepository;
        private readonly Mock<IMapper> _mockMapper;
        private readonly Mock<ILogger<FieldValidationRuleService>> _mockLogger;
        private readonly FieldValidationRuleService _service;

        public FieldValidationRuleServiceTests()
        {
            _mockRuleRepository = new Mock<IFieldValidationRuleRepository>();
            _mockFieldRepository = new Mock<IFormFieldRepository>();
            _mockConfigRepository = new Mock<IFormConfigurationRepository>();
            _mockMapper = new Mock<IMapper>();
            _mockLogger = new Mock<ILogger<FieldValidationRuleService>>();

            var validationMethods = new List<IValidationMethod>();
            
            _service = new FieldValidationRuleService(
                _mockRuleRepository.Object,
                _mockFieldRepository.Object,
                _mockConfigRepository.Object,
                validationMethods,
                _mockMapper.Object,
                _mockLogger.Object);
        }

        // CRUD Tests
        
        [Fact]
        public async Task GetByIdAsync_WithValidId_ReturnsRule()
        {
            // Arrange
            int ruleId = 1;
            var rule = new FieldValidationRule { Id = ruleId, ValidationType = "Required" };
            var dto = new FieldValidationRuleDto { Id = ruleId, ValidationType = "Required" };
            
            _mockRuleRepository.Setup(r => r.GetByIdAsync(ruleId)).ReturnsAsync(rule);
            _mockMapper.Setup(m => m.Map<FieldValidationRuleDto>(rule)).Returns(dto);

            // Act
            var result = await _service.GetByIdAsync(ruleId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(ruleId, result.Id);
            _mockRuleRepository.Verify(r => r.GetByIdAsync(ruleId), Times.Once);
        }

        [Fact]
        public async Task GetByIdAsync_WithInvalidId_ReturnsNull()
        {
            // Arrange
            _mockRuleRepository.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((FieldValidationRule)null);

            // Act
            var result = await _service.GetByIdAsync(1);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetByIdAsync_WithZeroId_ReturnsNull()
        {
            // Act
            var result = await _service.GetByIdAsync(0);

            // Assert
            Assert.Null(result);
            _mockRuleRepository.Verify(r => r.GetByIdAsync(It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task GetByFormFieldIdAsync_ReturnsAllRulesForField()
        {
            // Arrange
            int fieldId = 1;
            var rules = new List<FieldValidationRule>
            {
                new FieldValidationRule { Id = 1, FormFieldId = fieldId, ValidationType = "Required" },
                new FieldValidationRule { Id = 2, FormFieldId = fieldId, ValidationType = "MinLength" }
            };
            var dtos = new List<FieldValidationRuleDto>
            {
                new FieldValidationRuleDto { Id = 1, FormFieldId = fieldId },
                new FieldValidationRuleDto { Id = 2, FormFieldId = fieldId }
            };

            _mockRuleRepository.Setup(r => r.GetByFormFieldIdAsync(fieldId)).ReturnsAsync(rules);
            _mockMapper.Setup(m => m.Map<IEnumerable<FieldValidationRuleDto>>(rules)).Returns(dtos);

            // Act
            var result = await _service.GetByFormFieldIdAsync(fieldId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count());
        }

        [Fact]
        public async Task GetByFormConfigurationIdAsync_ReturnsAllRulesForConfiguration()
        {
            // Arrange
            int configId = 1;
            var rules = new List<FieldValidationRule>
            {
                new FieldValidationRule { Id = 1 },
                new FieldValidationRule { Id = 2 }
            };
            var dtos = new List<FieldValidationRuleDto>
            {
                new FieldValidationRuleDto { Id = 1 },
                new FieldValidationRuleDto { Id = 2 }
            };

            _mockRuleRepository.Setup(r => r.GetByFormConfigurationIdAsync(configId)).ReturnsAsync(rules);
            _mockMapper.Setup(m => m.Map<IEnumerable<FieldValidationRuleDto>>(rules)).Returns(dtos);

            // Act
            var result = await _service.GetByFormConfigurationIdAsync(configId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count());
        }

        [Fact]
        public async Task CreateAsync_WithValidDto_ReturnsCreatedRule()
        {
            // Arrange
            var createDto = new CreateFieldValidationRuleDto 
            { 
                FormFieldId = 1, 
                ValidationType = "Required" 
            };
            var field = new FormField { Id = 1, FieldName = "TestField" };
            var entity = new FieldValidationRule { FormFieldId = 1, ValidationType = "Required" };
            var createdEntity = new FieldValidationRule { Id = 1, FormFieldId = 1, ValidationType = "Required" };
            var dto = new FieldValidationRuleDto { Id = 1, FormFieldId = 1, ValidationType = "Required" };

            _mockFieldRepository.Setup(f => f.GetByIdAsync(1)).ReturnsAsync(field);
            _mockRuleRepository.Setup(r => r.HasCircularDependencyAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(false);
            _mockMapper.Setup(m => m.Map<FieldValidationRule>(createDto)).Returns(entity);
            _mockRuleRepository.Setup(r => r.CreateAsync(entity)).ReturnsAsync(createdEntity);
            _mockMapper.Setup(m => m.Map<FieldValidationRuleDto>(createdEntity)).Returns(dto);

            // Act
            var result = await _service.CreateAsync(createDto);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(1, result.Id);
            _mockRuleRepository.Verify(r => r.CreateAsync(entity), Times.Once);
        }

        [Fact]
        public async Task CreateAsync_WithInvalidFieldId_ThrowsException()
        {
            // Arrange
            var createDto = new CreateFieldValidationRuleDto 
            { 
                FormFieldId = 999, 
                ValidationType = "Required" 
            };

            _mockFieldRepository.Setup(f => f.GetByIdAsync(999)).ReturnsAsync((FormField)null);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(createDto));
        }

        [Fact]
        public async Task CreateAsync_WithInvalidDependencyFieldId_ThrowsException()
        {
            // Arrange
            var createDto = new CreateFieldValidationRuleDto 
            { 
                FormFieldId = 1, 
                DependsOnFieldId = 999,
                ValidationType = "Required" 
            };
            var field = new FormField { Id = 1 };

            _mockFieldRepository.Setup(f => f.GetByIdAsync(1)).ReturnsAsync(field);
            _mockFieldRepository.Setup(f => f.GetByIdAsync(999)).ReturnsAsync((FormField)null);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(createDto));
        }

        [Fact]
        public async Task CreateAsync_WithCircularDependency_ThrowsException()
        {
            // Arrange
            var createDto = new CreateFieldValidationRuleDto 
            { 
                FormFieldId = 1, 
                DependsOnFieldId = 2,
                ValidationType = "Required" 
            };
            var field1 = new FormField { Id = 1 };
            var field2 = new FormField { Id = 2 };

            _mockFieldRepository.Setup(f => f.GetByIdAsync(1)).ReturnsAsync(field1);
            _mockFieldRepository.Setup(f => f.GetByIdAsync(2)).ReturnsAsync(field2);
            _mockRuleRepository.Setup(r => r.HasCircularDependencyAsync(1, 2)).ReturnsAsync(true);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(createDto));
        }

        [Fact]
        public async Task UpdateAsync_WithValidDto_UpdatesRule()
        {
            // Arrange
            int ruleId = 1;
            var updateDto = new UpdateFieldValidationRuleDto { ValidationType = "MinLength" };
            var existing = new FieldValidationRule 
            { 
                Id = ruleId, 
                FormFieldId = 1, 
                ValidationType = "Required" 
            };

            _mockRuleRepository.Setup(r => r.GetByIdAsync(ruleId)).ReturnsAsync(existing);
            _mockRuleRepository.Setup(r => r.HasCircularDependencyAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(false);
            _mockMapper.Setup(m => m.Map(updateDto, existing)).Returns(existing);

            // Act
            await _service.UpdateAsync(ruleId, updateDto);

            // Assert
            _mockRuleRepository.Verify(r => r.UpdateAsync(existing), Times.Once);
        }

        [Fact]
        public async Task UpdateAsync_WithNonexistentRule_ThrowsException()
        {
            // Arrange
            var updateDto = new UpdateFieldValidationRuleDto { ValidationType = "MinLength" };
            _mockRuleRepository.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((FieldValidationRule)null);

            // Act & Assert
            await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpdateAsync(999, updateDto));
        }

        [Fact]
        public async Task DeleteAsync_WithValidId_DeletesRule()
        {
            // Arrange
            int ruleId = 1;
            var existing = new FieldValidationRule { Id = ruleId };

            _mockRuleRepository.Setup(r => r.GetByIdAsync(ruleId)).ReturnsAsync(existing);

            // Act
            await _service.DeleteAsync(ruleId);

            // Assert
            _mockRuleRepository.Verify(r => r.DeleteAsync(ruleId), Times.Once);
        }

        [Fact]
        public async Task DeleteAsync_WithNonexistentRule_ThrowsException()
        {
            // Arrange
            _mockRuleRepository.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((FieldValidationRule)null);

            // Act & Assert
            await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.DeleteAsync(999));
        }

        // Health Check Tests

        [Fact]
        public async Task ValidateConfigurationHealthAsync_AllRulesHealthy_ReturnsEmpty()
        {
            // Arrange
            int configId = 1;
            var config = new FormConfiguration
            {
                Id = configId,
                Steps = new List<FormStep>
                {
                    new FormStep
                    {
                        Id = 1,
                        Fields = new List<FormField>
                        {
                            new FormField { Id = 1, FieldGuid = Guid.NewGuid(), FieldName = "Field1" }
                        }
                    }
                }
            };
            var rules = new List<FieldValidationRule>
            {
                new FieldValidationRule 
                { 
                    Id = 1, 
                    FormFieldId = 1, 
                    ValidationType = "Required",
                    DependsOnFieldId = null
                }
            };

            _mockConfigRepository.Setup(c => c.GetByIdAsync(configId)).ReturnsAsync(config);
            _mockRuleRepository.Setup(r => r.GetByFormConfigurationIdAsync(configId)).ReturnsAsync(rules);

            var mockValidationMethod = new Mock<IValidationMethod>();
            mockValidationMethod.Setup(m => m.ValidationType).Returns("Required");
            var validationMethods = new List<IValidationMethod> { mockValidationMethod.Object };

            var service = new FieldValidationRuleService(
                _mockRuleRepository.Object,
                _mockFieldRepository.Object,
                _mockConfigRepository.Object,
                validationMethods,
                _mockMapper.Object,
                _mockLogger.Object);

            // Act
            var result = await service.ValidateConfigurationHealthAsync(configId);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task ValidateConfigurationHealthAsync_WithOrphanedRule_ReturnsError()
        {
            // Arrange
            int configId = 1;
            var config = new FormConfiguration
            {
                Id = configId,
                Steps = new List<FormStep>
                {
                    new FormStep
                    {
                        Id = 1,
                        Fields = new List<FormField>
                        {
                            new FormField { Id = 1, FieldGuid = Guid.NewGuid() }
                        }
                    }
                }
            };
            var rules = new List<FieldValidationRule>
            {
                new FieldValidationRule 
                { 
                    Id = 1, 
                    FormFieldId = 1, 
                    ValidationType = "Required",
                    DependsOnFieldId = 999 // Non-existent field
                }
            };

            _mockConfigRepository.Setup(c => c.GetByIdAsync(configId)).ReturnsAsync(config);
            _mockRuleRepository.Setup(r => r.GetByFormConfigurationIdAsync(configId)).ReturnsAsync(rules);

            var mockValidationMethod = new Mock<IValidationMethod>();
            mockValidationMethod.Setup(m => m.ValidationType).Returns("Required");
            var validationMethods = new List<IValidationMethod> { mockValidationMethod.Object };

            var service = new FieldValidationRuleService(
                _mockRuleRepository.Object,
                _mockFieldRepository.Object,
                _mockConfigRepository.Object,
                validationMethods,
                _mockMapper.Object,
                _mockLogger.Object);

            // Act
            var result = await service.ValidateConfigurationHealthAsync(configId);

            // Assert
            Assert.NotEmpty(result);
            Assert.Contains(result, i => i.Severity == "Error" && i.Message.Contains("non-existent"));
        }

        // A child form's rule may depend on a field of the form it's opened from (e.g. the siege
        // spawnpoint's Location on the scenario form's Town): that field exists, just not here.
        [Fact]
        public async Task ValidateConfigurationHealthAsync_DependencyOnAParentFormField_IsNotAnIssue()
        {
            int configId = 29;
            var config = new FormConfiguration
            {
                Id = configId,
                Steps = new List<FormStep>
                {
                    new FormStep { Id = 1, Fields = new List<FormField> { new FormField { Id = 2901, FieldGuid = Guid.NewGuid() } } }
                }
            };
            var rules = new List<FieldValidationRule>
            {
                new FieldValidationRule
                {
                    Id = 1,
                    FormFieldId = 2901,
                    ValidationType = "LocationInsideRegion",
                    DependsOnFieldId = 3201,
                    DependsOnField = new FormField { Id = 3201, FieldName = "TownId" }
                }
            };
            _mockConfigRepository.Setup(c => c.GetByIdAsync(configId)).ReturnsAsync(config);
            _mockRuleRepository.Setup(r => r.GetByFormConfigurationIdAsync(configId)).ReturnsAsync(rules);
            var mockValidationMethod = new Mock<IValidationMethod>();
            mockValidationMethod.Setup(m => m.ValidationType).Returns("LocationInsideRegion");

            var service = new FieldValidationRuleService(
                _mockRuleRepository.Object,
                _mockFieldRepository.Object,
                _mockConfigRepository.Object,
                new List<IValidationMethod> { mockValidationMethod.Object },
                _mockMapper.Object,
                _mockLogger.Object);

            Assert.Empty(await service.ValidateConfigurationHealthAsync(configId));
            Assert.Empty(await service.ValidateDraftConfigurationAsync(new FormConfigurationDto
            {
                Id = configId.ToString(),
                Steps = new List<FormStepDto> { new FormStepDto { Fields = new List<FormFieldDto> { new FormFieldDto { Id = "2901" } } } }
            }));
        }

        [Fact]
        public async Task ValidateConfigurationHealthAsync_WithUnknownValidationType_ReturnsError()
        {
            // Arrange
            int configId = 1;
            var config = new FormConfiguration
            {
                Id = configId,
                Steps = new List<FormStep>
                {
                    new FormStep
                    {
                        Id = 1,
                        Fields = new List<FormField>
                        {
                            new FormField { Id = 1, FieldGuid = Guid.NewGuid() }
                        }
                    }
                }
            };
            var rules = new List<FieldValidationRule>
            {
                new FieldValidationRule 
                { 
                    Id = 1, 
                    FormFieldId = 1, 
                    ValidationType = "UnknownType"
                }
            };

            _mockConfigRepository.Setup(c => c.GetByIdAsync(configId)).ReturnsAsync(config);
            _mockRuleRepository.Setup(r => r.GetByFormConfigurationIdAsync(configId)).ReturnsAsync(rules);

            var validationMethods = new List<IValidationMethod>();

            var service = new FieldValidationRuleService(
                _mockRuleRepository.Object,
                _mockFieldRepository.Object,
                _mockConfigRepository.Object,
                validationMethods,
                _mockMapper.Object,
                _mockLogger.Object);

            // Act
            var result = await service.ValidateConfigurationHealthAsync(configId);

            // Assert
            Assert.NotEmpty(result);
            Assert.Contains(result, i => i.Severity == "Error" && i.Message.Contains("Unknown validation type"));
        }

        [Fact]
        public async Task ValidateDraftConfigurationAsync_ValidDraft_ReturnsEmpty()
        {
            // Arrange
            var configDto = new FormConfigurationDto
            {
                Id = "1",
                Steps = new List<FormStepDto>
                {
                    new FormStepDto
                    {
                        Id = "1",
                        Fields = new List<FormFieldDto>
                        {
                            new FormFieldDto { Id = "1", FieldGuid = Guid.NewGuid().ToString() }
                        }
                    }
                }
            };
            var rules = new List<FieldValidationRule>();

            _mockRuleRepository.Setup(r => r.GetByFormConfigurationIdAsync(1)).ReturnsAsync(rules);

            // Act
            var result = await _service.ValidateDraftConfigurationAsync(configDto);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task ValidateDraftConfigurationAsync_EmptySteps_ReturnsEmpty()
        {
            // Arrange
            var configDto = new FormConfigurationDto
            {
                Id = "1",
                Steps = new List<FormStepDto>()
            };

            // Act
            var result = await _service.ValidateDraftConfigurationAsync(configDto);

            // Assert
            Assert.Empty(result);
        }

        // Dependency Analysis Tests

        [Fact]
        public async Task GetDependentFieldIdsAsync_ReturnsDependentFields()
        {
            // Arrange
            int fieldId = 1;
            var rules = new List<FieldValidationRule>
            {
                new FieldValidationRule { Id = 1, FormFieldId = 2, DependsOnFieldId = fieldId },
                new FieldValidationRule { Id = 2, FormFieldId = 3, DependsOnFieldId = fieldId },
                new FieldValidationRule { Id = 3, FormFieldId = 2, DependsOnFieldId = fieldId } // Duplicate
            };

            _mockRuleRepository.Setup(r => r.GetRulesDependingOnFieldAsync(fieldId)).ReturnsAsync(rules);

            // Act
            var result = await _service.GetDependentFieldIdsAsync(fieldId);

            // Assert
            Assert.NotNull(result);
            var resultList = result.ToList();
            Assert.Equal(2, resultList.Count);
            Assert.Contains(2, resultList);
            Assert.Contains(3, resultList);
        }

        [Fact]
        public async Task GetDependentFieldIdsAsync_WithNoDependencies_ReturnsEmpty()
        {
            // Arrange
            int fieldId = 1;
            var rules = new List<FieldValidationRule>();

            _mockRuleRepository.Setup(r => r.GetRulesDependingOnFieldAsync(fieldId)).ReturnsAsync(rules);

            // Act
            var result = await _service.GetDependentFieldIdsAsync(fieldId);

            // Assert
            Assert.Empty(result);
        }
    }
}
