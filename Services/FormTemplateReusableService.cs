using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using AutoMapper;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Service for reusing FormStep and FormField templates across configurations.
    /// Handles both Copy and Link modes for template reuse.
    /// </summary>
    public interface IFormTemplateReusableService
    {
        /// <summary>
        /// Add a reusable step to a configuration (copy or link mode).
        /// </summary>
        Task<FormStepDto> AddReusableStepToConfigurationAsync(
            int formConfigurationId,
            int sourceStepId,
            ReuseLinkMode linkMode,
            IFormConfigurationRepository configRepo,
            IFormStepRepository stepRepo);

        /// <summary>
        /// Add a reusable field to a step (copy or link mode).
        /// </summary>
        Task<FormFieldDto> AddReusableFieldToStepAsync(
            int formStepId,
            int sourceFieldId,
            ReuseLinkMode linkMode,
            IFormStepRepository stepRepo,
            IFormFieldRepository fieldRepo);
    }

    public class FormTemplateReusableService : IFormTemplateReusableService
    {
        private readonly IMapper _mapper;

        public FormTemplateReusableService(IMapper mapper)
        {
            _mapper = mapper;
        }

        public async Task<FormStepDto> AddReusableStepToConfigurationAsync(
            int formConfigurationId,
            int sourceStepId,
            ReuseLinkMode linkMode,
            IFormConfigurationRepository configRepo,
            IFormStepRepository stepRepo)
        {
            // Load source step
            var sourceStep = await stepRepo.GetByIdAsync(sourceStepId);
            if (sourceStep == null)
                throw new KeyNotFoundException($"Source step with id {sourceStepId} not found.");

            if (!sourceStep.IsReusable)
                throw new InvalidOperationException($"Step {sourceStepId} is not marked as reusable.");

            // Load target configuration
            var config = await configRepo.GetByIdAsync(formConfigurationId);
            if (config == null)
                throw new KeyNotFoundException($"FormConfiguration with id {formConfigurationId} not found.");

            FormStep newStep;

            if (linkMode == ReuseLinkMode.Link)
            {
                // Link mode: Create lightweight reference to source
                newStep = CreateLinkedStep(sourceStep, formConfigurationId);
            }
            else
            {
                // Copy mode (default): Clone the entire step
                newStep = CloneStep(sourceStep, formConfigurationId);
            }

            // Add to repository
            await stepRepo.AddAsync(newStep);

            // Add to configuration's StepOrderJson
            await UpdateStepOrder(config, newStep.StepGuid, configRepo);

            // For linked steps, populate fields from source before mapping to DTO
            // This ensures the frontend receives the field data even though it's a reference
            if (linkMode == ReuseLinkMode.Link)
            {
                newStep.Fields = sourceStep.Fields;
            }

            return _mapper.Map<FormStepDto>(newStep);
        }

        public async Task<FormFieldDto> AddReusableFieldToStepAsync(
            int formStepId,
            int sourceFieldId,
            ReuseLinkMode linkMode,
            IFormStepRepository stepRepo,
            IFormFieldRepository fieldRepo)
        {
            // Load source field
            var sourceField = await fieldRepo.GetByIdAsync(sourceFieldId);
            if (sourceField == null)
                throw new KeyNotFoundException($"Source field with id {sourceFieldId} not found.");

            if (!sourceField.IsReusable)
                throw new InvalidOperationException($"Field {sourceFieldId} is not marked as reusable.");

            // Load target step
            var step = await stepRepo.GetByIdAsync(formStepId);
            if (step == null)
                throw new KeyNotFoundException($"FormStep with id {formStepId} not found.");

            FormField newField;

            if (linkMode == ReuseLinkMode.Link)
            {
                // Link mode: Create lightweight reference to source
                newField = CreateLinkedField(sourceField, formStepId);
            }
            else
            {
                // Copy mode (default): Clone the entire field
                newField = CloneField(sourceField, formStepId);
            }

            // Add to repository
            await fieldRepo.AddAsync(newField);

            // Add to step's FieldOrderJson
            await UpdateFieldOrder(step, newField.FieldGuid, stepRepo);

            // For linked fields, copy validation data from source before mapping to DTO
            // This ensures the frontend receives validation rules even though it's a reference
            if (linkMode == ReuseLinkMode.Link && sourceField.Validations != null)
            {
                newField.Validations = sourceField.Validations.ToList();
            }

            return _mapper.Map<FormFieldDto>(newField);
        }

        /// <summary>
        /// Clone a FormStep: create a full independent copy.
        /// </summary>
        private FormStep CloneStep(FormStep sourceStep, int newFormConfigurationId)
        {
            var clonedStep = new FormStep
            {
                StepGuid = Guid.NewGuid(), // New GUID for this instance
                StepName = sourceStep.StepName,
                Description = sourceStep.Description,
                IsReusable = false, // No longer reusable (it's a copy)
                SourceStepId = sourceStep.Id, // Track source for traceability
                IsLinkedToSource = false, // Copy mode
                FormConfigurationId = newFormConfigurationId,
                FieldOrderJson = sourceStep.FieldOrderJson, // Clone field order
                Fields = new List<FormField>(),
                StepConditions = new List<StepCondition>()
            };

            // Clone all fields
            var fieldIdMapping = new Dictionary<int, int>(); // Old ID -> New ID
            foreach (var sourceField in sourceStep.Fields)
            {
                var clonedField = CloneField(sourceField, clonedStep.Id);
                clonedStep.Fields.Add(clonedField);
                // Note: We'd need to track new IDs for field dependency updates
            }

            // Clone conditions
            foreach (var condition in sourceStep.StepConditions)
            {
                clonedStep.StepConditions.Add(new StepCondition
                {
                    FormStepId = clonedStep.Id,
                    ConditionType = condition.ConditionType,
                    ConditionLogicJson = condition.ConditionLogicJson,
                    ErrorMessage = condition.ErrorMessage
                });
            }

            return clonedStep;
        }

        /// <summary>
        /// Create a linked FormStep: lightweight reference to source.
        /// </summary>
        private FormStep CreateLinkedStep(FormStep sourceStep, int newFormConfigurationId)
        {
            return new FormStep
            {
                StepGuid = Guid.NewGuid(), // New GUID for ordering in this configuration
                StepName = sourceStep.StepName, // Initially from source
                Description = sourceStep.Description,
                IsReusable = false,
                SourceStepId = sourceStep.Id,
                IsLinkedToSource = true, // Link mode flag
                FormConfigurationId = newFormConfigurationId,
                FieldOrderJson = "[]", // Will be customized per configuration if needed
                Fields = new List<FormField>(), // Fields loaded from source at read-time
                StepConditions = new List<StepCondition>()
            };
        }

        /// <summary>
        /// Clone a FormField: create a full independent copy.
        /// </summary>
        private FormField CloneField(FormField sourceField, int newFormStepId)
        {
            var clonedField = new FormField
            {
                FieldGuid = Guid.NewGuid(), // New GUID for this instance
                FieldName = sourceField.FieldName,
                Label = sourceField.Label,
                Placeholder = sourceField.Placeholder,
                Description = sourceField.Description,
                FieldType = sourceField.FieldType,
                ElementType = sourceField.ElementType,
                ObjectType = sourceField.ObjectType,
                EnumType = sourceField.EnumType,
                DefaultValue = sourceField.DefaultValue,
                Required = sourceField.Required,
                ReadOnly = sourceField.ReadOnly,
                SettingsJson = sourceField.SettingsJson,
                IsReusable = false, // No longer reusable
                SourceFieldId = sourceField.Id, // Track source for traceability
                IsLinkedToSource = false, // Copy mode
                FormStepId = newFormStepId,
                DependsOnFieldId = sourceField.DependsOnFieldId, // Keep dependency (may need adjustment)
                DependencyConditionJson = sourceField.DependencyConditionJson,
                SubConfigurationId = sourceField.SubConfigurationId,
                MinSelection = sourceField.MinSelection,
                MaxSelection = sourceField.MaxSelection,
                CanCreate = sourceField.CanCreate,
                IncrementValue = sourceField.IncrementValue,
                Validations = new List<FieldValidation>(),
                DependentFields = new List<FormField>()
            };

            // Clone validations
            foreach (var validation in sourceField.Validations)
            {
                clonedField.Validations.Add(new FieldValidation
                {
                    FormFieldId = clonedField.Id,
                    Type = validation.Type,
                    ParametersJson = validation.ParametersJson,
                    ErrorMessage = validation.ErrorMessage
                });
            }

            return clonedField;
        }

        /// <summary>
        /// Create a linked FormField: lightweight reference to source.
        /// </summary>
        private FormField CreateLinkedField(FormField sourceField, int newFormStepId)
        {
            return new FormField
            {
                FieldGuid = Guid.NewGuid(), // New GUID for ordering in this step
                FieldName = sourceField.FieldName, // Initially from source
                Label = sourceField.Label,
                Placeholder = sourceField.Placeholder,
                Description = sourceField.Description,
                FieldType = sourceField.FieldType,
                ElementType = sourceField.ElementType,
                ObjectType = sourceField.ObjectType,
                EnumType = sourceField.EnumType,
                DefaultValue = sourceField.DefaultValue,
                Required = sourceField.Required,
                ReadOnly = sourceField.ReadOnly,
                SettingsJson = sourceField.SettingsJson,
                IsReusable = false,
                SourceFieldId = sourceField.Id,
                IsLinkedToSource = true, // Link mode flag
                FormStepId = newFormStepId,
                DependsOnFieldId = sourceField.DependsOnFieldId,
                DependencyConditionJson = sourceField.DependencyConditionJson,
                SubConfigurationId = sourceField.SubConfigurationId,
                MinSelection = sourceField.MinSelection,
                MaxSelection = sourceField.MaxSelection,
                CanCreate = sourceField.CanCreate,
                IncrementValue = sourceField.IncrementValue,
                Validations = new List<FieldValidation>(), // Validations from source at read-time
                DependentFields = new List<FormField>()
            };
        }

        /// <summary>
        /// Update configuration's StepOrderJson to include the new step GUID.
        /// </summary>
        private async Task UpdateStepOrder(
            FormConfiguration config,
            Guid newStepGuid,
            IFormConfigurationRepository configRepo)
        {
            try
            {
                var orderList = JsonSerializer.Deserialize<List<string>>(config.StepOrderJson ?? "[]") ?? new List<string>();
                orderList.Add(newStepGuid.ToString());
                config.StepOrderJson = JsonSerializer.Serialize(orderList);
                config.UpdatedAt = DateTime.UtcNow;
                await configRepo.UpdateAsync(config);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to update step order: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Update step's FieldOrderJson to include the new field GUID.
        /// </summary>
        private async Task UpdateFieldOrder(
            FormStep step,
            Guid newFieldGuid,
            IFormStepRepository stepRepo)
        {
            try
            {
                var orderList = JsonSerializer.Deserialize<List<string>>(step.FieldOrderJson ?? "[]") ?? new List<string>();
                orderList.Add(newFieldGuid.ToString());
                step.FieldOrderJson = JsonSerializer.Serialize(orderList);
                await stepRepo.UpdateAsync(step);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to update field order: {ex.Message}", ex);
            }
        }
    }
}
