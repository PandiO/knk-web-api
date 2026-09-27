using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Service implementation for field validation rule CRUD and management operations.
    /// Handles rule creation, updates, deletion, and configuration health validation.
    /// Complements ValidationService which handles rule execution.
    /// </summary>
    public class FieldValidationRuleService : IFieldValidationRuleService
    {
        private readonly IFieldValidationRuleRepository _ruleRepository;
        private readonly IFormFieldRepository _fieldRepository;
        private readonly IFormConfigurationRepository _configRepository;
        private readonly IEnumerable<IValidationMethod> _validationMethods;
        private readonly IMapper _mapper;
        private readonly ILogger<FieldValidationRuleService> _logger;

        public FieldValidationRuleService(
            IFieldValidationRuleRepository ruleRepository,
            IFormFieldRepository fieldRepository,
            IFormConfigurationRepository configRepository,
            IEnumerable<IValidationMethod> validationMethods,
            IMapper mapper,
            ILogger<FieldValidationRuleService> logger)
        {
            _ruleRepository = ruleRepository ?? throw new ArgumentNullException(nameof(ruleRepository));
            _fieldRepository = fieldRepository ?? throw new ArgumentNullException(nameof(fieldRepository));
            _configRepository = configRepository ?? throw new ArgumentNullException(nameof(configRepository));
            _validationMethods = validationMethods ?? throw new ArgumentNullException(nameof(validationMethods));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // CRUD Operations
        public async Task<FieldValidationRuleDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var entity = await _ruleRepository.GetByIdAsync(id);
            return entity == null ? null : _mapper.Map<FieldValidationRuleDto>(entity);
        }

        public async Task<IEnumerable<FieldValidationRuleDto>> GetByFormFieldIdAsync(int fieldId)
        {
            var list = await _ruleRepository.GetByFormFieldIdAsync(fieldId);
            return _mapper.Map<IEnumerable<FieldValidationRuleDto>>(list);
        }

        public async Task<IEnumerable<FieldValidationRuleDto>> GetByFormConfigurationIdAsync(int formConfigurationId)
        {
            var list = await _ruleRepository.GetByFormConfigurationIdAsync(formConfigurationId);
            return _mapper.Map<IEnumerable<FieldValidationRuleDto>>(list);
        }

        public async Task<IEnumerable<FieldValidationRuleDto>> GetByFormFieldIdWithDependenciesAsync(
            int fieldId,
            Dictionary<string, object>? formContext = null)
        {
            var rules = await _ruleRepository.GetByFormFieldIdAsync(fieldId);
            return _mapper.Map<IEnumerable<FieldValidationRuleDto>>(rules);
        }

        public async Task<FieldValidationRuleDto> CreateAsync(CreateFieldValidationRuleDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            _logger.LogInformation("Creating validation rule for field {FieldId}", dto.FormFieldId);

            var field = await _fieldRepository.GetByIdAsync(dto.FormFieldId);
            if (field == null)
            {
                _logger.LogError("FormField {FieldId} not found", dto.FormFieldId);
                throw new ArgumentException($"FormField {dto.FormFieldId} not found", nameof(dto.FormFieldId));
            }

            if (dto.DependsOnFieldId.HasValue)
            {
                var dependsOn = await _fieldRepository.GetByIdAsync(dto.DependsOnFieldId.Value);
                if (dependsOn == null)
                {
                    _logger.LogError("DependsOnField {FieldId} not found", dto.DependsOnFieldId.Value);
                    throw new ArgumentException($"DependsOnField {dto.DependsOnFieldId.Value} not found", nameof(dto.DependsOnFieldId));
                }

                var hasCircular = await _ruleRepository.HasCircularDependencyAsync(dto.FormFieldId, dto.DependsOnFieldId.Value);
                if (hasCircular)
                {
                    _logger.LogError("Circular dependency detected between fields {FieldId} and {DependsOnFieldId}",
                        dto.FormFieldId, dto.DependsOnFieldId.Value);
                    throw new ArgumentException("Circular dependency detected between fields.");
                }
            }

            var entity = _mapper.Map<FieldValidationRule>(dto);
            var created = await _ruleRepository.CreateAsync(entity);
            
            _logger.LogInformation("Validation rule {RuleId} created successfully", created.Id);
            
            return _mapper.Map<FieldValidationRuleDto>(created);
        }

        public async Task UpdateAsync(int id, UpdateFieldValidationRuleDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            _logger.LogInformation("Updating validation rule {RuleId}", id);

            var existing = await _ruleRepository.GetByIdAsync(id) ?? throw new KeyNotFoundException($"Rule {id} not found");

            if (dto.DependsOnFieldId.HasValue)
            {
                var dependsOn = await _fieldRepository.GetByIdAsync(dto.DependsOnFieldId.Value);
                if (dependsOn == null)
                {
                    _logger.LogError("DependsOnField {FieldId} not found", dto.DependsOnFieldId.Value);
                    throw new ArgumentException($"DependsOnField {dto.DependsOnFieldId.Value} not found", nameof(dto.DependsOnFieldId));
                }

                var hasCircular = await _ruleRepository.HasCircularDependencyAsync(existing.FormFieldId, dto.DependsOnFieldId.Value);
                if (hasCircular)
                {
                    _logger.LogError("Circular dependency detected");
                    throw new ArgumentException("Circular dependency detected between fields.");
                }
            }

            _mapper.Map(dto, existing);
            await _ruleRepository.UpdateAsync(existing);
            
            _logger.LogInformation("Validation rule {RuleId} updated successfully", id);
        }

        public async Task DeleteAsync(int id)
        {
            _logger.LogInformation("Deleting validation rule {RuleId}", id);

            var existing = await _ruleRepository.GetByIdAsync(id) ?? throw new KeyNotFoundException($"Rule {id} not found");
            await _ruleRepository.DeleteAsync(existing.Id);
            
            _logger.LogInformation("Validation rule {RuleId} deleted successfully", id);
        }

        // Health Check Operations
        public async Task<IEnumerable<ValidationIssueDto>> ValidateConfigurationHealthAsync(int formConfigurationId)
        {
            _logger.LogInformation("Validating configuration health for {ConfigId}", formConfigurationId);
            
            var issues = new List<ValidationIssueDto>();

            // Get all validation rules in this configuration
            var rules = await _ruleRepository.GetByFormConfigurationIdAsync(formConfigurationId);
            
            // Get the form configuration with all steps and fields
            var config = await _configRepository.GetByIdAsync(formConfigurationId);
            if (config == null)
            {
                issues.Add(new ValidationIssueDto
                {
                    Severity = "Error",
                    Message = $"Form configuration with ID {formConfigurationId} not found"
                });
                return issues;
            }

            // Build a field order map (stepIndex, fieldIndex in step)
            // CRITICAL: Use FieldOrderJson to get the correct visual order, not database order
            var fieldOrderMap = new Dictionary<int, (int stepIndex, int fieldIndex)>();
            for (int stepIdx = 0; stepIdx < config.Steps.Count; stepIdx++)
            {
                var step = config.Steps[stepIdx];
                
                // Get the ordered fields based on FieldOrderJson
                var orderedFields = GetOrderedFields(step);
                
                for (int fieldIdx = 0; fieldIdx < orderedFields.Count; fieldIdx++)
                {
                    var field = orderedFields[fieldIdx];
                    fieldOrderMap[field.Id] = (stepIdx, fieldIdx);
                }
            }

            foreach (var rule in rules)
            {
                // Check 1: Dependency field exists
                // A field of another configuration is a parent-form field: a child form's rule may
                // depend on a field of the form it is opened from (the wizard and saved-entity checks
                // resolve it from the parent's values), so it has no position to check here.
                var isParentFormDependency = rule.DependsOnFieldId.HasValue
                    && !fieldOrderMap.ContainsKey(rule.DependsOnFieldId.Value)
                    && rule.DependsOnField != null;
                if (rule.DependsOnFieldId.HasValue && !isParentFormDependency)
                {
                    if (!fieldOrderMap.ContainsKey(rule.DependsOnFieldId.Value))
                    {
                        issues.Add(new ValidationIssueDto
                        {
                            Severity = "Error",
                            Message = $"Validation rule {rule.Id} references non-existent dependency field ID {rule.DependsOnFieldId.Value}",
                            FieldId = rule.FormFieldId,
                            RuleId = rule.Id
                        });
                        continue;
                    }

                    // Check 2: Dependency field comes before dependent field
                    var dependentFieldOrder = fieldOrderMap[rule.FormFieldId];
                    var dependencyFieldOrder = fieldOrderMap[rule.DependsOnFieldId.Value];

                    if (dependencyFieldOrder.stepIndex > dependentFieldOrder.stepIndex)
                    {
                        issues.Add(new ValidationIssueDto
                        {
                            Severity = "Warning",
                            Message = $"Dependency field (ID {rule.DependsOnFieldId.Value}) appears AFTER dependent field (ID {rule.FormFieldId}). Consider reordering fields.",
                            FieldId = rule.FormFieldId,
                            RuleId = rule.Id
                        });
                    }
                    else if (dependencyFieldOrder.stepIndex == dependentFieldOrder.stepIndex 
                             && dependencyFieldOrder.fieldIndex > dependentFieldOrder.fieldIndex)
                    {
                        issues.Add(new ValidationIssueDto
                        {
                            Severity = "Warning",
                            Message = $"Dependency field appears after dependent field in the same step. Users may need to fill fields in reverse order.",
                            FieldId = rule.FormFieldId,
                            RuleId = rule.Id
                        });
                    }

                    // Check 3: Circular dependency
                    var hasCircular = await _ruleRepository.HasCircularDependencyAsync(
                        rule.FormFieldId, 
                        rule.DependsOnFieldId.Value);
                    
                    if (hasCircular)
                    {
                        issues.Add(new ValidationIssueDto
                        {
                            Severity = "Error",
                            Message = $"Circular dependency detected: Field {rule.FormFieldId} → {rule.DependsOnFieldId.Value}",
                            FieldId = rule.FormFieldId,
                            RuleId = rule.Id
                        });
                    }
                }

                // Check 4: Validation method exists
                var validationMethod = _validationMethods.FirstOrDefault(m => m.ValidationType == rule.ValidationType);
                if (validationMethod == null)
                {
                    issues.Add(new ValidationIssueDto
                    {
                        Severity = "Error",
                        Message = $"Unknown validation type: {rule.ValidationType}",
                        FieldId = rule.FormFieldId,
                        RuleId = rule.Id
                    });
                }
            }

            _logger.LogInformation("Configuration health check completed with {IssueCount} issues", issues.Count);
            
            return issues;
        }

        public async Task<IEnumerable<ValidationIssueDto>> ValidateDraftConfigurationAsync(FormConfigurationDto configDto)
        {
            _logger.LogInformation("Validating draft configuration");
            
            var issues = new List<ValidationIssueDto>();

            if (configDto.Steps == null || !configDto.Steps.Any())
            {
                return issues;
            }

            // For draft validation, we focus on field ordering issues based on stored validation rules
            // We can't validate dependency rules from FieldValidationDto since they don't include dependency info
            // Instead, we check if any saved rules exist for fields and whether ordering is correct

            // If config has ID, load its validation rules from database
            if (!string.IsNullOrEmpty(configDto.Id) && int.TryParse(configDto.Id, out var configId))
            {
                // Load validation rules for this configuration
                var rules = await _ruleRepository.GetByFormConfigurationIdAsync(configId);
                
                // Build field position map from draft config
                var fieldPositionMap = new Dictionary<int, (int stepIndex, int fieldIndex)>();
                
                for (int stepIdx = 0; stepIdx < configDto.Steps.Count; stepIdx++)
                {
                    var step = configDto.Steps[stepIdx];
                    var orderedFieldDtos = GetOrderedFieldDtos(step);
                    
                    for (int fieldIdx = 0; fieldIdx < orderedFieldDtos.Count; fieldIdx++)
                    {
                        var fieldDto = orderedFieldDtos[fieldIdx];
                        if (!string.IsNullOrEmpty(fieldDto.Id) && int.TryParse(fieldDto.Id, out var fieldId))
                        {
                            fieldPositionMap[fieldId] = (stepIdx, fieldIdx);
                        }
                    }
                }

                // Check each rule's dependency ordering
                foreach (var rule in rules)
                {
                    if (!rule.DependsOnFieldId.HasValue)
                        continue;

                    if (!fieldPositionMap.ContainsKey(rule.FormFieldId))
                    {
                        issues.Add(new ValidationIssueDto
                        {
                            Severity = "Error",
                            Message = $"Validation rule {rule.Id} references field ID {rule.FormFieldId} which is not in the current configuration",
                            FieldId = rule.FormFieldId,
                            RuleId = rule.Id
                        });
                        continue;
                    }

                    // A parent-form dependency (field of another configuration) has no position here.
                    if (!fieldPositionMap.ContainsKey(rule.DependsOnFieldId.Value) && rule.DependsOnField != null)
                        continue;

                    if (!fieldPositionMap.ContainsKey(rule.DependsOnFieldId.Value))
                    {
                        issues.Add(new ValidationIssueDto
                        {
                            Severity = "Error",
                            Message = $"Validation rule {rule.Id} references non-existent dependency field ID {rule.DependsOnFieldId.Value}",
                            FieldId = rule.FormFieldId,
                            RuleId = rule.Id
                        });
                        continue;
                    }

                    // Check dependency field ordering
                    var dependentFieldOrder = fieldPositionMap[rule.FormFieldId];
                    var dependencyFieldOrder = fieldPositionMap[rule.DependsOnFieldId.Value];

                    if (dependencyFieldOrder.stepIndex > dependentFieldOrder.stepIndex)
                    {
                        issues.Add(new ValidationIssueDto
                        {
                            Severity = "Warning",
                            Message = $"Dependency field (ID {rule.DependsOnFieldId.Value}) appears AFTER dependent field (ID {rule.FormFieldId}). Consider reordering fields.",
                            FieldId = rule.FormFieldId,
                            RuleId = rule.Id
                        });
                    }
                    else if (dependencyFieldOrder.stepIndex == dependentFieldOrder.stepIndex && 
                             dependencyFieldOrder.fieldIndex >= dependentFieldOrder.fieldIndex)
                    {
                        issues.Add(new ValidationIssueDto
                        {
                            Severity = "Warning",
                            Message = $"Dependency field (ID {rule.DependsOnFieldId.Value}) appears AFTER or at same position as dependent field (ID {rule.FormFieldId}) in step {dependentFieldOrder.stepIndex + 1}. Reorder fields.",
                            FieldId = rule.FormFieldId,
                            RuleId = rule.Id
                        });
                    }
                }
            }

            return issues;
        }

        // Dependency Analysis
        public async Task<IEnumerable<int>> GetDependentFieldIdsAsync(int fieldId)
        {
            var rules = await _ruleRepository.GetRulesDependingOnFieldAsync(fieldId);
            return rules.Select(r => r.FormFieldId).Distinct();
        }

        // Helper Methods
        /// <summary>
        /// Get fields in the correct visual order based on FieldOrderJson for DTOs.
        /// </summary>
        private List<FormFieldDto> GetOrderedFieldDtos(FormStepDto step)
        {
            if (string.IsNullOrWhiteSpace(step.FieldOrderJson) || step.Fields == null)
            {
                return step.Fields?.ToList() ?? new List<FormFieldDto>();
            }

            try
            {
                var guidOrder = System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(step.FieldOrderJson);
                if (guidOrder == null || guidOrder.Count == 0)
                {
                    return step.Fields.ToList();
                }

                var fieldMap = new Dictionary<Guid, FormFieldDto>();
                foreach (var field in step.Fields)
                {
                    if (!string.IsNullOrEmpty(field.FieldGuid) && Guid.TryParse(field.FieldGuid, out var parsedGuid))
                    {
                        fieldMap[parsedGuid] = field;
                    }
                }

                var reordered = new List<FormFieldDto>();
                foreach (var guid in guidOrder)
                {
                    if (fieldMap.TryGetValue(guid, out var field))
                    {
                        reordered.Add(field);
                    }
                }

                foreach (var field in step.Fields)
                {
                    if (!reordered.Contains(field))
                    {
                        reordered.Add(field);
                    }
                }

                return reordered;
            }
            catch
            {
                return step.Fields.ToList();
            }
        }

        /// <summary>
        /// Get fields in the correct visual order based on FieldOrderJson.
        /// Falls back to database order if FieldOrderJson is not available.
        /// </summary>
        private List<FormField> GetOrderedFields(FormStep step)
        {
            if (string.IsNullOrWhiteSpace(step.FieldOrderJson))
            {
                return step.Fields.ToList();
            }

            try
            {
                var guidOrder = System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(step.FieldOrderJson);
                if (guidOrder == null || guidOrder.Count == 0)
                {
                    return step.Fields.ToList();
                }

                var fieldMap = step.Fields.ToDictionary(f => f.FieldGuid, f => f);

                var reordered = new List<FormField>();
                foreach (var guid in guidOrder)
                {
                    if (fieldMap.TryGetValue(guid, out var field))
                    {
                        reordered.Add(field);
                    }
                }

                foreach (var field in step.Fields)
                {
                    if (!reordered.Contains(field))
                    {
                        reordered.Add(field);
                    }
                }

                return reordered;
            }
            catch
            {
                return step.Fields.ToList();
            }
        }
    }
}
