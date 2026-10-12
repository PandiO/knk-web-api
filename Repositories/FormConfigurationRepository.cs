using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class FormConfigurationRepository : IFormConfigurationRepository
    {
        private readonly KnKDbContext _context;

        public FormConfigurationRepository(KnKDbContext context)
        {
            _context = context;
        }

        private static IQueryable<FormConfiguration> WithFullGraph(IQueryable<FormConfiguration> query)
        {
            return query
                .Include(fc => fc.Steps)
                    .ThenInclude(s => s.Fields)
                        .ThenInclude(f => f.Validations)
                .Include(fc => fc.Steps)
                    .ThenInclude(s => s.Fields)
                        .ThenInclude(f => f.DisplayConditionGroups)
                            .ThenInclude(g => g.Conditions)
                .Include(fc => fc.Steps)
                    .ThenInclude(s => s.StepConditions)
                .Include(fc => fc.Steps)
                    .ThenInclude(s => s.DisplayConditionGroups)
                        .ThenInclude(g => g.Conditions)
                .Include(fc => fc.Steps)
                    .ThenInclude(s => s.ChildFormSteps)
                        .ThenInclude(cs => cs.Fields)
                            .ThenInclude(f => f.Validations);
        }

        public async Task<IEnumerable<FormConfiguration>> GetAllAsync()
        {
            return await WithFullGraph(_context.FormConfigurations).ToListAsync();
        }

        public async Task<FormConfiguration?> GetByIdAsync(int id)
        {
            return await WithFullGraph(_context.FormConfigurations)
                .FirstOrDefaultAsync(fc => fc.Id == id);
        }

        public async Task<IEnumerable<FormConfiguration>> GetAllByEntityTypeNameAsync(string entityName, bool defaultOnly = false)
        {
            var query = WithFullGraph(_context.FormConfigurations)
                .Where(fc => fc.EntityTypeName == entityName);

            if (defaultOnly)
            {
                query = query.Where(fc => fc.IsDefault);
            }

            return await query.ToListAsync();
        }

        public async Task<IEnumerable<FormConfiguration>> GetAllByEntityTypeNameAllAsync(string entityName)
        {
            return await WithFullGraph(_context.FormConfigurations)
                .Where(fc => fc.EntityTypeName == entityName)
                .ToListAsync();
        }

        public async Task<FormConfiguration?> GetDefaultByEntityTypeNameAsync(string entityName)
        {
            return await WithFullGraph(_context.FormConfigurations)
                .FirstOrDefaultAsync(fc => fc.EntityTypeName == entityName && fc.IsDefault);
        }

        public async Task AddAsync(FormConfiguration config)
        {
            var pendingGroups = DetachDisplayConditionGroups(config);

            await _context.FormConfigurations.AddAsync(config);
            await _context.SaveChangesAsync();

            RestoreDisplayConditionGroups(pendingGroups);
            ResolveDisplayConditionSourceFields(config);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(FormConfiguration config)
        {
            // Load existing tracked graph
            var existing = await WithFullGraph(_context.FormConfigurations)
                .FirstOrDefaultAsync(fc => fc.Id == config.Id);

            if (existing == null)
            {
                return; // or throw if desired
            }

            // Update scalar properties
            existing.Name = config.Name;
            existing.EntityTypeName = config.EntityTypeName;
            existing.Description = config.Description;
            existing.IsDefault = config.IsDefault;
            existing.StepOrderJson = config.StepOrderJson;
            existing.UpdatedAt = config.UpdatedAt;

            // ----- Merge Steps -----
            var incomingStepsById = config.Steps.Where(s => s.Id != 0).ToDictionary(s => s.Id);
            // Remove steps not present
            foreach (var removeStep in existing.Steps.Where(s => !incomingStepsById.ContainsKey(s.Id)).ToList())
            {
                _context.FormSteps.Remove(removeStep);
            }
            // Process incoming steps
            foreach (var step in config.Steps)
            {
                var match = step.Id != 0 ? existing.Steps.FirstOrDefault(s => s.Id == step.Id) : null;
                if (match == null)
                {
                    // New step
                    var newStep = new FormStep
                    {
                        StepGuid = step.StepGuid != Guid.Empty ? step.StepGuid : Guid.NewGuid(),
                        StepName = step.StepName,
                        Description = step.Description,
                        IsReusable = step.IsReusable,
                        SourceStepId = step.SourceStepId,
                        IsLinkedToSource = step.IsLinkedToSource,
                        FieldOrderJson = step.FieldOrderJson,
                        IsManyToManyRelationship = step.IsManyToManyRelationship,
                        RelatedEntityPropertyName = step.RelatedEntityPropertyName,
                        JoinEntityType = step.JoinEntityType,
                        SubConfigurationId = step.SubConfigurationId,
                        ParentStepId = step.ParentStepId,
                        FormConfigurationId = existing.Id,
                        // StepGuid stays auto-generated
                        CreatedAt = DateTime.UtcNow
                    };
                    existing.Steps.Add(newStep);
                    match = newStep;
                }
                else
                {
                    // Update existing step scalars
                    match.StepName = step.StepName;
                    match.Description = step.Description;
                    match.IsReusable = step.IsReusable;
                    match.SourceStepId = step.SourceStepId;
                    match.IsLinkedToSource = step.IsLinkedToSource;
                    if (step.StepGuid != Guid.Empty)
                        match.StepGuid = step.StepGuid;
                    match.FieldOrderJson = step.FieldOrderJson;
                    match.IsManyToManyRelationship = step.IsManyToManyRelationship;
                    match.RelatedEntityPropertyName = step.RelatedEntityPropertyName;
                    match.JoinEntityType = step.JoinEntityType;
                    match.SubConfigurationId = step.SubConfigurationId;
                    match.ParentStepId = step.ParentStepId;
                }

                // ----- Merge StepConditions -----
                var incomingConditionsIds = step.StepConditions.Where(c => c.Id != 0).Select(c => c.Id).ToHashSet();
                foreach (var removeCond in match.StepConditions.Where(c => !incomingConditionsIds.Contains(c.Id)).ToList())
                    _context.StepConditions.Remove(removeCond);

                foreach (var cond in step.StepConditions)
                {
                    var condMatch = cond.Id != 0 ? match.StepConditions.FirstOrDefault(c => c.Id == cond.Id) : null;
                    if (condMatch == null)
                    {
                        match.StepConditions.Add(new StepCondition
                        {
                            ConditionType = cond.ConditionType,
                            ConditionLogicJson = cond.ConditionLogicJson,
                            ErrorMessage = cond.ErrorMessage,
                            FormStepId = match.Id
                        });
                    }
                    else
                    {
                        condMatch.ConditionType = cond.ConditionType;
                        condMatch.ConditionLogicJson = cond.ConditionLogicJson;
                        condMatch.ErrorMessage = cond.ErrorMessage;
                    }
                }

                // ----- Merge Fields -----
                var incomingFieldIds = step.Fields.Where(f => f.Id != 0).Select(f => f.Id).ToHashSet();
                foreach (var removeField in match.Fields.Where(f => !incomingFieldIds.Contains(f.Id)).ToList())
                    _context.FormFields.Remove(removeField);

                foreach (var field in step.Fields)
                {
                    var fieldMatch = field.Id != 0 ? match.Fields.FirstOrDefault(f => f.Id == field.Id) : null;
                    if (fieldMatch == null)
                    {
                        var newField = new FormField
                        {
                            FieldGuid = field.FieldGuid != Guid.Empty ? field.FieldGuid : Guid.NewGuid(),
                            FieldName = field.FieldName,
                            Label = field.Label,
                            Placeholder = field.Placeholder,
                            Description = field.Description,
                            FieldType = field.FieldType,
                            ElementType = field.ElementType,
                            ObjectType = field.ObjectType,
                            EnumType = field.EnumType,
                            DefaultValue = field.DefaultValue,
                            Required = field.Required,
                            ReadOnly = field.ReadOnly,
                            SettingsJson = field.SettingsJson,
                            IsReusable = field.IsReusable,
                            SourceFieldId = field.SourceFieldId,
                            IsLinkedToSource = field.IsLinkedToSource,
                            DependsOnFieldId = field.DependsOnFieldId,
                            DependencyConditionJson = field.DependencyConditionJson,
                            SubConfigurationId = field.SubConfigurationId,
                            MinSelection = field.MinSelection,
                            MaxSelection = field.MaxSelection,
                            CanCreate = field.CanCreate,
                            IncrementValue = field.IncrementValue,
                            FormStepId = match.Id,
                            CreatedAt = DateTime.UtcNow
                        };
                        match.Fields.Add(newField);
                        fieldMatch = newField;
                    }
                    else
                    {
                        fieldMatch.FieldName = field.FieldName;
                        fieldMatch.Label = field.Label;
                        fieldMatch.Placeholder = field.Placeholder;
                        fieldMatch.Description = field.Description;
                        fieldMatch.FieldType = field.FieldType;
                        fieldMatch.ElementType = field.ElementType;
                        fieldMatch.ObjectType = field.ObjectType;
                        fieldMatch.EnumType = field.EnumType;
                        fieldMatch.DefaultValue = field.DefaultValue;
                        fieldMatch.Required = field.Required;
                        fieldMatch.ReadOnly = field.ReadOnly;
                        fieldMatch.SettingsJson = field.SettingsJson;
                        fieldMatch.IsReusable = field.IsReusable;
                        fieldMatch.SourceFieldId = field.SourceFieldId;
                        fieldMatch.IsLinkedToSource = field.IsLinkedToSource;
                        if (field.FieldGuid != Guid.Empty)
                            fieldMatch.FieldGuid = field.FieldGuid;
                        fieldMatch.DependsOnFieldId = field.DependsOnFieldId;
                        fieldMatch.DependencyConditionJson = field.DependencyConditionJson;
                        fieldMatch.SubConfigurationId = field.SubConfigurationId;
                        fieldMatch.MinSelection = field.MinSelection;
                        fieldMatch.MaxSelection = field.MaxSelection;
                        fieldMatch.CanCreate = field.CanCreate;
                        fieldMatch.IncrementValue = field.IncrementValue;
                    }

                    // ----- Merge Validations -----
                    var incomingValidationIds = field.Validations.Where(v => v.Id != 0).Select(v => v.Id).ToHashSet();
                    foreach (var removeVal in fieldMatch.Validations.Where(v => !incomingValidationIds.Contains(v.Id)).ToList())
                        _context.FieldValidations.Remove(removeVal);

                    foreach (var val in field.Validations)
                    {
                        var valMatch = val.Id != 0 ? fieldMatch.Validations.FirstOrDefault(v => v.Id == val.Id) : null;
                        if (valMatch == null)
                        {
                            fieldMatch.Validations.Add(new FieldValidation
                            {
                                Type = val.Type,
                                ParametersJson = val.ParametersJson,
                                ErrorMessage = val.ErrorMessage,
                                FormFieldId = fieldMatch.Id
                            });
                        }
                        else
                        {
                            valMatch.Type = val.Type;
                            valMatch.ParametersJson = val.ParametersJson;
                            valMatch.ErrorMessage = val.ErrorMessage;
                        }
                    }
                }

                // ----- Merge ChildFormSteps (for M2M relationships) -----
                var incomingChildStepIds = step.ChildFormSteps.Where(cs => cs.Id != 0).Select(cs => cs.Id).ToHashSet();
                foreach (var removeChild in match.ChildFormSteps.Where(cs => !incomingChildStepIds.Contains(cs.Id)).ToList())
                    _context.FormSteps.Remove(removeChild);

                foreach (var childStep in step.ChildFormSteps)
                {
                    var childMatch = childStep.Id != 0 ? match.ChildFormSteps.FirstOrDefault(cs => cs.Id == childStep.Id) : null;
                    if (childMatch == null)
                    {
                        var newChild = new FormStep
                        {
                            StepGuid = childStep.StepGuid != Guid.Empty ? childStep.StepGuid : Guid.NewGuid(),
                            StepName = childStep.StepName,
                            Description = childStep.Description,
                            IsReusable = childStep.IsReusable,
                            SourceStepId = childStep.SourceStepId,
                            IsLinkedToSource = childStep.IsLinkedToSource,
                            FieldOrderJson = childStep.FieldOrderJson,
                            SubConfigurationId = childStep.SubConfigurationId,
                            ParentStepId = match.Id,
                            CreatedAt = DateTime.UtcNow
                        };
                        match.ChildFormSteps.Add(newChild);
                        childMatch = newChild;
                    }
                    else
                    {
                        childMatch.StepName = childStep.StepName;
                        childMatch.Description = childStep.Description;
                        childMatch.IsReusable = childStep.IsReusable;
                        childMatch.SourceStepId = childStep.SourceStepId;
                        childMatch.IsLinkedToSource = childStep.IsLinkedToSource;
                        if (childStep.StepGuid != Guid.Empty)
                            childMatch.StepGuid = childStep.StepGuid;
                        childMatch.FieldOrderJson = childStep.FieldOrderJson;
                        childMatch.SubConfigurationId = childStep.SubConfigurationId;
                    }

                    // Merge child step fields
                    var incomingChildFieldIds = childStep.Fields.Where(f => f.Id != 0).Select(f => f.Id).ToHashSet();
                    foreach (var removeField in childMatch.Fields.Where(f => !incomingChildFieldIds.Contains(f.Id)).ToList())
                        _context.FormFields.Remove(removeField);

                    foreach (var field in childStep.Fields)
                    {
                        var fieldMatch = field.Id != 0 ? childMatch.Fields.FirstOrDefault(f => f.Id == field.Id) : null;
                        if (fieldMatch == null)
                        {
                            var newField = new FormField
                            {
                                FieldGuid = field.FieldGuid != Guid.Empty ? field.FieldGuid : Guid.NewGuid(),
                                FieldName = field.FieldName,
                                Label = field.Label,
                                Placeholder = field.Placeholder,
                                Description = field.Description,
                                FieldType = field.FieldType,
                                ElementType = field.ElementType,
                                ObjectType = field.ObjectType,
                                EnumType = field.EnumType,
                                DefaultValue = field.DefaultValue,
                                Required = field.Required,
                                ReadOnly = field.ReadOnly,
                                SettingsJson = field.SettingsJson,
                                IsReusable = field.IsReusable,
                                SourceFieldId = field.SourceFieldId,
                                IsLinkedToSource = field.IsLinkedToSource,
                                DependsOnFieldId = field.DependsOnFieldId,
                                DependencyConditionJson = field.DependencyConditionJson,
                                SubConfigurationId = field.SubConfigurationId,
                                MinSelection = field.MinSelection,
                                MaxSelection = field.MaxSelection,
                                CanCreate = field.CanCreate,
                                IncrementValue = field.IncrementValue,
                                FormStepId = childMatch.Id,
                                CreatedAt = DateTime.UtcNow
                            };
                            childMatch.Fields.Add(newField);
                            fieldMatch = newField;
                        }
                        else
                        {
                            fieldMatch.FieldName = field.FieldName;
                            fieldMatch.Label = field.Label;
                            fieldMatch.Placeholder = field.Placeholder;
                            fieldMatch.Description = field.Description;
                            fieldMatch.FieldType = field.FieldType;
                            fieldMatch.ElementType = field.ElementType;
                            fieldMatch.ObjectType = field.ObjectType;
                            fieldMatch.EnumType = field.EnumType;
                            fieldMatch.DefaultValue = field.DefaultValue;
                            fieldMatch.Required = field.Required;
                            fieldMatch.ReadOnly = field.ReadOnly;
                            fieldMatch.SettingsJson = field.SettingsJson;
                            fieldMatch.IsReusable = field.IsReusable;
                            fieldMatch.SourceFieldId = field.SourceFieldId;
                            fieldMatch.IsLinkedToSource = field.IsLinkedToSource;
                            if (field.FieldGuid != Guid.Empty)
                                fieldMatch.FieldGuid = field.FieldGuid;
                            fieldMatch.DependsOnFieldId = field.DependsOnFieldId;
                            fieldMatch.DependencyConditionJson = field.DependencyConditionJson;
                            fieldMatch.SubConfigurationId = field.SubConfigurationId;
                            fieldMatch.MinSelection = field.MinSelection;
                            fieldMatch.MaxSelection = field.MaxSelection;
                            fieldMatch.CanCreate = field.CanCreate;
                            fieldMatch.IncrementValue = field.IncrementValue;
                        }

                        // Merge child field validations
                        var incomingValidationIds = field.Validations.Where(v => v.Id != 0).Select(v => v.Id).ToHashSet();
                        foreach (var removeVal in fieldMatch.Validations.Where(v => !incomingValidationIds.Contains(v.Id)).ToList())
                            _context.FieldValidations.Remove(removeVal);

                        foreach (var val in field.Validations)
                        {
                            var valMatch = val.Id != 0 ? fieldMatch.Validations.FirstOrDefault(v => v.Id == val.Id) : null;
                            if (valMatch == null)
                            {
                                fieldMatch.Validations.Add(new FieldValidation
                                {
                                    Type = val.Type,
                                    ParametersJson = val.ParametersJson,
                                    ErrorMessage = val.ErrorMessage,
                                    FormFieldId = fieldMatch.Id
                                });
                            }
                            else
                            {
                                valMatch.Type = val.Type;
                                valMatch.ParametersJson = val.ParametersJson;
                                valMatch.ErrorMessage = val.ErrorMessage;
                            }
                        }
                    }
                }
            }

            await _context.SaveChangesAsync();

            // Second pass: display conditions reference fields by GUID, so they can only be
            // resolved once every field in this save has a database id.
            SyncDisplayConditionGroups(existing, config);
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Temporarily removes the condition groups from a brand new graph so EF does not try to
        /// insert them with an unresolved SourceFormFieldId.
        /// </summary>
        private static List<(FormStep? Step, FormField? Field, List<DisplayConditionGroup> Groups)> DetachDisplayConditionGroups(FormConfiguration config)
        {
            var pending = new List<(FormStep?, FormField?, List<DisplayConditionGroup>)>();

            foreach (var step in config.Steps)
            {
                if (step.DisplayConditionGroups.Count > 0)
                {
                    pending.Add((step, null, step.DisplayConditionGroups.ToList()));
                    step.DisplayConditionGroups.Clear();
                }

                foreach (var field in step.Fields)
                {
                    if (field.DisplayConditionGroups.Count > 0)
                    {
                        pending.Add((null, field, field.DisplayConditionGroups.ToList()));
                        field.DisplayConditionGroups.Clear();
                    }
                }
            }

            return pending;
        }

        private static void RestoreDisplayConditionGroups(List<(FormStep? Step, FormField? Field, List<DisplayConditionGroup> Groups)> pending)
        {
            foreach (var (step, field, groups) in pending)
            {
                foreach (var group in groups)
                {
                    if (step != null)
                    {
                        group.TargetType = DisplayConditionTargetType.FormStep;
                        group.TargetStepId = step.Id;
                        group.TargetFieldId = null;
                        step.DisplayConditionGroups.Add(group);
                    }
                    else if (field != null)
                    {
                        group.TargetType = DisplayConditionTargetType.FormField;
                        group.TargetFieldId = field.Id;
                        group.TargetStepId = null;
                        field.DisplayConditionGroups.Add(group);
                    }
                }
            }
        }

        /// <summary>
        /// Replaces the persisted condition groups of every step and field with the incoming set.
        /// Groups are cheap value-like configuration, so a clear-and-rebuild keeps the merge simple
        /// and avoids stale rows pointing at removed fields.
        /// </summary>
        private void SyncDisplayConditionGroups(FormConfiguration existing, FormConfiguration incoming)
        {
            var incomingStepsByGuid = incoming.Steps.ToDictionary(s => s.StepGuid);

            foreach (var step in existing.Steps)
            {
                if (!incomingStepsByGuid.TryGetValue(step.StepGuid, out var incomingStep)) continue;

                ReplaceGroups(step.DisplayConditionGroups, incomingStep.DisplayConditionGroups,
                    DisplayConditionTargetType.FormStep, step.Id, null);

                var incomingFieldsByGuid = incomingStep.Fields
                    .GroupBy(f => f.FieldGuid)
                    .ToDictionary(g => g.Key, g => g.First());

                foreach (var field in step.Fields)
                {
                    if (!incomingFieldsByGuid.TryGetValue(field.FieldGuid, out var incomingField)) continue;

                    ReplaceGroups(field.DisplayConditionGroups, incomingField.DisplayConditionGroups,
                        DisplayConditionTargetType.FormField, null, field.Id);
                }
            }

            ResolveDisplayConditionSourceFields(existing);
        }

        private void ReplaceGroups(
            List<DisplayConditionGroup> current,
            List<DisplayConditionGroup> incoming,
            DisplayConditionTargetType targetType,
            int? targetStepId,
            int? targetFieldId)
        {
            foreach (var stale in current.ToList())
            {
                _context.DisplayConditionGroups.Remove(stale);
            }
            current.Clear();

            foreach (var group in incoming)
            {
                current.Add(new DisplayConditionGroup
                {
                    TargetType = targetType,
                    TargetStepId = targetStepId,
                    TargetFieldId = targetFieldId,
                    InnerLogic = group.InnerLogic,
                    CombineWithPreviousLogic = group.CombineWithPreviousLogic,
                    Order = group.Order,
                    IsActive = group.IsActive,
                    Conditions = group.Conditions.Select(c => new DisplayCondition
                    {
                        SourceFormFieldGuid = c.SourceFormFieldGuid,
                        Operator = c.Operator,
                        ValueJson = c.ValueJson,
                        Order = c.Order
                    }).ToList()
                });
            }
        }

        private static void ResolveDisplayConditionSourceFields(FormConfiguration config)
        {
            var fieldIdByGuid = config.Steps
                .SelectMany(s => s.Fields)
                .GroupBy(f => f.FieldGuid)
                .ToDictionary(g => g.Key, g => g.First().Id);

            var groups = config.Steps.SelectMany(s => s.DisplayConditionGroups)
                .Concat(config.Steps.SelectMany(s => s.Fields).SelectMany(f => f.DisplayConditionGroups));

            foreach (var condition in groups.SelectMany(g => g.Conditions))
            {
                if (fieldIdByGuid.TryGetValue(condition.SourceFormFieldGuid, out var fieldId))
                {
                    condition.SourceFormFieldId = fieldId;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Display condition references unknown source field '{condition.SourceFormFieldGuid}'.");
                }
            }
        }

        public async Task DeleteAsync(int id)
        {
            var config = await _context.FormConfigurations.FindAsync(id);
            if (config != null)
            {
                _context.FormConfigurations.Remove(config);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<IEnumerable<string>> GetEntityTypeNamesAsync()
        {
            return await _context.FormConfigurations
                .Select(fc => fc.EntityTypeName)
                .Distinct()
                .ToListAsync();
        }
    }
}

