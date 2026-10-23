using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.ValidationMethods;
using knkwebapi_v2.Services.ValidationResultLog;
using knkwebapi_v2.Configuration;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Service implementation for field validation operations.
    /// Coordinates validation rule execution and result formatting.
    /// </summary>
    public class ValidationService : IValidationService
    {
        private readonly IFieldValidationRuleRepository _ruleRepository;
        private readonly IEnumerable<IValidationMethod> _validationMethods;
        private readonly IPlaceholderResolutionService _placeholderService;
        private readonly IValidationResultLog? _resultLog;
        private readonly ValidationResultLogOptions _logOptions;

        public ValidationService(
            IFieldValidationRuleRepository ruleRepository,
            IEnumerable<IValidationMethod> validationMethods,
            IPlaceholderResolutionService placeholderService)
            : this(ruleRepository, validationMethods, placeholderService, null, null)
        {
        }

        /// <param name="resultLog">KNG-269: null runs every rule every time (as before the log existed).</param>
        public ValidationService(
            IFieldValidationRuleRepository ruleRepository,
            IEnumerable<IValidationMethod> validationMethods,
            IPlaceholderResolutionService placeholderService,
            IValidationResultLog? resultLog,
            IOptions<ValidationResultLogOptions>? logOptions)
        {
            _ruleRepository = ruleRepository;
            _validationMethods = validationMethods;
            _placeholderService = placeholderService;
            _resultLog = resultLog;
            _logOptions = logOptions?.Value ?? new ValidationResultLogOptions();
        }

        public Task<ValidationResultDto> ValidateFieldAsync(
            int fieldId,
            object? fieldValue,
            object? dependencyValue,
            Dictionary<string, object>? formContextData)
            => ValidateFieldAsync(fieldId, fieldValue, dependencyValue, formContextData, null);

        public async Task<ValidationResultDto> ValidateFieldAsync(
            int fieldId,
            object? fieldValue,
            object? dependencyValue,
            Dictionary<string, object>? formContextData,
            ValidationSubject? subject,
            bool force = false)
        {
            // Get all validation rules for this field
            var rules = await _ruleRepository.GetByFormFieldIdAsync(fieldId);
            
            Console.WriteLine($"[VALIDATION_TRACE_BACKEND] Validating field {fieldId}");
            Console.WriteLine($"[VALIDATION_TRACE_BACKEND]   fieldValue: {fieldValue ?? "null"}");
            Console.WriteLine($"[VALIDATION_TRACE_BACKEND]   dependencyValue: {dependencyValue ?? "null"}");
            Console.WriteLine($"[VALIDATION_TRACE_BACKEND]   rulesCount: {rules.Count()}");
            if (formContextData != null)
            {
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]   formContextData keys: {string.Join(", ", formContextData.Keys)}");
                foreach (var kvp in formContextData)
                {
                    Console.WriteLine($"[VALIDATION_TRACE_BACKEND]     {kvp.Key}: {kvp.Value ?? "null"}");
                }
            }
            
            if (!rules.Any())
            {
                // No rules = validation passes
                return new ValidationResultDto
                {
                    IsValid = true,
                    IsBlocking = false,
                    Message = "No validation rules configured"
                };
            }

            // Collect all placeholders from all rules
            var aggregatedPlaceholders = new Dictionary<string, object>();

            // Execute each rule and collect results
            var results = new List<ValidationResultDto>();
            
            foreach (var rule in rules)
            {
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]   Executing rule {rule.Id} (type: {rule.ValidationType})");
                
                // Resolve placeholders for this rule if needed
                var resolvedPlaceholders = await ResolvePlaceholdersForRuleAsync(rule, formContextData);
                
                var result = await ExecuteValidationRuleAsync(rule, fieldValue, dependencyValue, formContextData, resolvedPlaceholders, subject, force);
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]   Rule {rule.Id} result: isValid={result.IsValid}, isBlocking={result.IsBlocking}, fromCache={result.Metadata?.FromCache ?? false}");
                
                // Aggregate placeholders from this rule
                if (result.Placeholders != null)
                {
                    foreach (var placeholder in result.Placeholders)
                    {
                        aggregatedPlaceholders[placeholder.Key] = placeholder.Value;
                    }
                }
                
                results.Add(result);
            }

            // If any blocking rule failed, return the first failure (with aggregated placeholders up to that point)
            var blockingFailure = results.FirstOrDefault(r => !r.IsValid && r.IsBlocking);
            if (blockingFailure != null)
            {
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND] Field {fieldId} blocking failure: {blockingFailure.Message}");
                // Include placeholders from all rules executed so far
                blockingFailure.Placeholders = aggregatedPlaceholders.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString() ?? "");
                return blockingFailure;
            }

            // If any non-blocking rule failed, return the first warning (with aggregated placeholders)
            var nonBlockingFailure = results.FirstOrDefault(r => !r.IsValid && !r.IsBlocking);
            if (nonBlockingFailure != null)
            {
                nonBlockingFailure.Placeholders = aggregatedPlaceholders.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString() ?? "");
                return nonBlockingFailure;
            }

            // All rules passed
            var successMessages = results.Where(r => r.IsValid && !string.IsNullOrEmpty(r.Message)).Select(r => r.Message).ToList();
            return new ValidationResultDto
            {
                IsValid = true,
                IsBlocking = false,
                Message = successMessages.Any() ? string.Join("; ", successMessages) : "Validation passed",
                Placeholders = aggregatedPlaceholders.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString() ?? ""),
                Metadata = CombinedMetadata(results)
            };
        }

        /// <summary>
        /// KNG-269: a field whose rules all passed is "from cache" only when every rule was; its time is
        /// the oldest stored run, which is what "checked … ago" must show.
        /// </summary>
        private static ValidationMetadataDto? CombinedMetadata(List<ValidationResultDto> results)
        {
            var withMetadata = results.Where(r => r.Metadata != null).Select(r => r.Metadata!).ToList();
            if (withMetadata.Count == 0) return null;
            var oldest = withMetadata.OrderBy(m => m.ExecutedAt, StringComparer.Ordinal).First();
            return new ValidationMetadataDto
            {
                ValidationType = string.Join(";", withMetadata.Select(m => m.ValidationType).Distinct()),
                ExecutedAt = oldest.ExecutedAt,
                FromCache = withMetadata.All(m => m.FromCache)
            };
        }

        public async Task<ValidationResultDto> ValidateFieldAsync(ValidateFieldRequestDto request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return await ValidateFieldAsync(
                request.FieldId,
                request.FieldValue,
                request.DependencyValue,
                request.FormContextData,
                ValidationSubject.From(request.SubjectType, request.SubjectId),
                request.Force);
        }

        public async Task<ValidateFieldsBatchResultDto> ValidateFieldsAsync(ValidateFieldsBatchRequestDto request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var subject = ValidationSubject.From(request.SubjectType, request.SubjectId);
            var batch = new ValidateFieldsBatchResultDto();
            // Sequential on purpose: one scoped DbContext serves the whole request.
            foreach (var item in request.Fields)
            {
                batch.Results[item.FieldId] = await ValidateFieldAsync(
                    item.FieldId, item.FieldValue, item.DependencyValue, request.FormContextData, subject, request.Force);
            }
            return batch;
        }

        public async Task<Dictionary<int, ValidationResultDto>> ValidateMultipleFieldsAsync(
            IEnumerable<int> fieldIds,
            Dictionary<int, object?> fieldValues,
            Dictionary<string, object>? formContextData)
        {
            var results = new Dictionary<int, ValidationResultDto>();

            foreach (var fieldId in fieldIds)
            {
                fieldValues.TryGetValue(fieldId, out var fieldValue);
                
                // For multi-field validation, we need to determine dependency values
                // This is a simplified implementation - may need enhancement
                var result = await ValidateFieldAsync(fieldId, fieldValue, null, formContextData);
                results[fieldId] = result;
            }

            return results;
        }

        public async Task<ValidationResultDto> ValidateFieldWithPlaceholdersAsync(
            int fieldId,
            object? fieldValue,
            object? dependencyValue,
            Dictionary<string, object>? formContextData)
        {
            // This method is identical to ValidateFieldAsync since we enhanced it to aggregate placeholders
            // We keep this explicit method for clarity and to match the interface contract
            return await ValidateFieldAsync(fieldId, fieldValue, dependencyValue, formContextData);
        }

        /// <summary>
        /// Resolve placeholders for a specific validation rule.
        /// Only resolves if the rule requires dependency filling and has placeholder-capable messages.
        /// </summary>
        private async Task<Dictionary<string, object>?> ResolvePlaceholdersForRuleAsync(
            FieldValidationRule rule,
            Dictionary<string, object>? contextData)
        {
            // If no context data or rule doesn't require dependencies, no placeholders to resolve
            if (contextData == null || !rule.RequiresDependencyFilled)
            {
                return null;
            }

            // Check if rule messages contain placeholders
            var hasPlaceholders = 
                (!string.IsNullOrEmpty(rule.ErrorMessage) && rule.ErrorMessage.Contains("{")) ||
                (!string.IsNullOrEmpty(rule.SuccessMessage) && rule.SuccessMessage.Contains("{"));

            if (!hasPlaceholders)
            {
                return null;
            }

            try
            {
                // Use placeholder resolution service to resolve all placeholders in the messages
                var placeholders = new Dictionary<string, object>();
                
                // Extract placeholders from error message
                if (!string.IsNullOrEmpty(rule.ErrorMessage))
                {
                    var errorPlaceholders = await _placeholderService.ExtractPlaceholdersAsync(rule.ErrorMessage);
                    foreach (var placeholder in errorPlaceholders)
                    {
                        if (contextData.TryGetValue(placeholder, out var value))
                        {
                            placeholders[placeholder] = value;
                        }
                    }
                }

                // Extract placeholders from success message
                if (!string.IsNullOrEmpty(rule.SuccessMessage))
                {
                    var successPlaceholders = await _placeholderService.ExtractPlaceholdersAsync(rule.SuccessMessage);
                    foreach (var placeholder in successPlaceholders)
                    {
                        if (contextData.TryGetValue(placeholder, out var value) && !placeholders.ContainsKey(placeholder))
                        {
                            placeholders[placeholder] = value;
                        }
                    }
                }

                return placeholders.Any() ? placeholders : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND] Error resolving placeholders for rule {rule.Id}: {ex.Message}");
                return null; // Fail-open: don't block validation if placeholder resolution fails
            }
        }

        /// <summary>
        /// Merge placeholder dictionaries, with validation method placeholders taking precedence.
        /// </summary>
        private Dictionary<string, string> MergePlaceholders(
            Dictionary<string, string>? validationMethodPlaceholders,
            Dictionary<string, object>? resolvedPlaceholders)
        {
            var merged = new Dictionary<string, string>();

            // Add resolved placeholders first (lower precedence)
            if (resolvedPlaceholders != null)
            {
                foreach (var kvp in resolvedPlaceholders)
                {
                    merged[kvp.Key] = kvp.Value?.ToString() ?? "";
                }
            }

            // Add validation method placeholders (higher precedence - overwrites if key exists)
            if (validationMethodPlaceholders != null)
            {
                foreach (var kvp in validationMethodPlaceholders)
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }

            return merged;
        }

        /// <summary>
        /// When the dependency field is an entity reference (an Object field), the form often holds
        /// only part of the picked record - an edit loads a read DTO's FK plus display name as
        /// {id, name} - while validators read other properties off it (Town.WgRegionId). So a
        /// dependency value that carries an id is replaced by the saved record. A loaded entity (the
        /// saved-entity checks pass one), an unsaved record without an id, or an id that isn't found
        /// stays as it is.
        /// </summary>
        private async Task<object?> ResolveDependencyRecordAsync(FieldValidationRule rule, object? dependencyValue)
        {
            var field = rule.DependsOnField;
            if (dependencyValue == null || field == null || field.FieldType != FieldType.Object || string.IsNullOrWhiteSpace(field.ObjectType))
                return dependencyValue;

            var id = ReferencedId(dependencyValue);
            if (id == null) return dependencyValue;

            try
            {
                return await _placeholderService.LoadEntityByIdAsync(field.ObjectType, id) ?? dependencyValue;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Could not load {field.ObjectType} {id}: {ex.Message}");
                return dependencyValue;
            }
        }

        // The id of a form value that references a record: a bare id, or form JSON / a dictionary
        // with an "id". Anything else (e.g. an entity object) has none to resolve.
        private static object? ReferencedId(object value)
        {
            switch (value)
            {
                case int or long:
                    return value;
                case string text:
                    return long.TryParse(text, out var parsed) ? parsed : null;
                case JsonElement { ValueKind: JsonValueKind.Number } number:
                    return number.TryGetInt64(out var n) ? n : null;
                case JsonElement { ValueKind: JsonValueKind.String } str:
                    return long.TryParse(str.GetString(), out var s) ? s : null;
                case JsonElement { ValueKind: JsonValueKind.Object } obj:
                    foreach (var property in obj.EnumerateObject())
                        if (property.Name.Equals("id", StringComparison.OrdinalIgnoreCase))
                            return ReferencedId(property.Value);
                    return null;
                case IDictionary<string, object?> dictionary:
                    var key = dictionary.Keys.FirstOrDefault(k => k.Equals("id", StringComparison.OrdinalIgnoreCase));
                    return key != null && dictionary[key] is { } idValue ? ReferencedId(idValue) : null;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Execute a single validation rule with optional placeholder resolution.
        /// </summary>
        /// <summary>
        /// The rule's ErrorMessage for a failed check; for a region rule (KNG-173) followed by what it
        /// must be inside ("Must be inside District "Old Quarter" (region domain_27)."), since the
        /// rule's text names no place.
        /// </summary>
        private static string FailureMessage(FieldValidationRule rule, ValidationMethodResult result)
        {
            var message = rule.ErrorMessage ?? result.Message;
            return RegionRuleMessages.AppliesTo(rule.ValidationType)
                ? RegionRuleMessages.WithParent(rule, message, result.Placeholders)
                : message;
        }

        private async Task<ValidationResultDto> ExecuteValidationRuleAsync(
            FieldValidationRule rule,
            object? fieldValue,
            object? dependencyValue,
            Dictionary<string, object>? formContextData,
            Dictionary<string, object>? resolvedPlaceholders,
            ValidationSubject? subject,
            bool force)
        {
            Console.WriteLine($"[VALIDATION_TRACE_BACKEND]     Rule execution started:");
            Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       DependsOnFieldId: {rule.DependsOnFieldId}");
            Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       DependsOnField.FieldName: {rule.DependsOnField?.FieldName}");
            Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       RequiresDependencyFilled: {rule.RequiresDependencyFilled}");
            
            // CRITICAL FIX: If dependencyValue is not provided but formContextData contains it, extract it
            if (rule.DependsOnFieldId.HasValue && dependencyValue == null && formContextData != null && rule.DependsOnField != null)
            {
                var dependencyFieldName = rule.DependsOnField.FieldName;
                if (!string.IsNullOrEmpty(dependencyFieldName) && formContextData.TryGetValue(dependencyFieldName, out var contextValue))
                {
                    dependencyValue = contextValue;
                    Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Extracted dependencyValue from formContextData['{dependencyFieldName}']");
                }
            }

            // The result echoes what the form sent, not the loaded record: a tracked entity's navigations
            // can point back at each other (a permission group and its parent), which the response
            // serialiser refuses as a cycle.
            var formDependencyValue = dependencyValue;
            dependencyValue = await ResolveDependencyRecordAsync(rule, dependencyValue);

            // Check if dependency is required but not filled
            if (rule.DependsOnFieldId.HasValue && dependencyValue == null && !rule.RequiresDependencyFilled)
            {
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Dependency not filled, RequiresDependencyFilled=false, returning valid");
                return new ValidationResultDto
                {
                    IsValid = true,
                    IsBlocking = false,
                    Message = "Validation pending: dependency field not filled",
                    Metadata = new ValidationMetadataDto
                    {
                        ValidationType = rule.ValidationType,
                        ExecutedAt = DateTime.UtcNow.ToString("o"),
                        DependencyFieldName = rule.DependsOnField?.FieldName
                    }
                };
            }

            // Find the validation method implementation
            var validationMethod = _validationMethods.FirstOrDefault(m => m.ValidationType == rule.ValidationType);
            if (validationMethod == null)
            {
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Validation method not found: {rule.ValidationType}");
                return new ValidationResultDto
                {
                    IsValid = false,
                    IsBlocking = true,
                    Message = $"Validation method not found: {rule.ValidationType}",
                    Metadata = new ValidationMetadataDto
                    {
                        ValidationType = rule.ValidationType,
                        ExecutedAt = DateTime.UtcNow.ToString("o")
                    }
                };
            }

            // KNG-269: answer from the log of the last run for this subject while the rule and the inputs
            // the validator reads are unchanged. The dependency record was loaded above either way (one
            // DB read); what is skipped is the validator itself, for the region rules a plugin round trip.
            string? ruleHash = null, inputHash = null;
            var log = subject != null && _logOptions.Enabled ? _resultLog : null;
            if (log != null)
            {
                ruleHash = ValidationInputHasher.RuleHash(rule);
                inputHash = ValidationInputHasher.InputHash(fieldValue,
                    ValidationInputHasher.ReduceDependency(dependencyValue, validationMethod.DependencyPropertiesRead(rule.ConfigJson)));
                if (!force)
                {
                    var stored = await log.FindAsync(rule.Id, subject!.Type, subject.Id);
                    if (stored != null && stored.RuleHash == ruleHash && stored.InputHash == inputHash && !Expired(stored, validationMethod))
                    {
                        Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Answered from the result log (run of {stored.ExecutedAt:o})");
                        return FromLog(stored, rule, formDependencyValue);
                    }
                }
            }

            // Execute the validation
            try
            {
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Calling {rule.ValidationType}.ValidateAsync()");
                var result = await validationMethod.ValidateAsync(
                    fieldValue,
                    dependencyValue,
                    rule.ConfigJson,
                    formContextData);

                // A validator can flag that it couldn't complete the check (e.g. the Minecraft
                // plugin is unreachable) rather than reaching a genuine pass/fail verdict. In
                // that case the validator's own message must be shown as-is — overwriting it
                // with the rule's configured ErrorMessage would misrepresent a system/connectivity
                // problem as a business-rule violation.
                var failureReason = result.Metadata != null && result.Metadata.TryGetValue("failureReason", out var reasonObj)
                    ? reasonObj?.ToString()
                    : null;

                var dto = new ValidationResultDto
                {
                    IsValid = result.IsValid,
                    IsBlocking = rule.IsBlocking,
                    Message = result.IsValid
                        ? (rule.SuccessMessage ?? result.Message)
                        : (failureReason != null ? result.Message : FailureMessage(rule, result)),
                    Placeholders = MergePlaceholders(result.Placeholders, resolvedPlaceholders),
                    Metadata = new ValidationMetadataDto
                    {
                        ValidationType = rule.ValidationType,
                        ExecutedAt = DateTime.UtcNow.ToString("o"),
                        DependencyFieldName = rule.DependsOnField?.FieldName,
                        DependencyValue = formDependencyValue,
                        FailureReason = failureReason
                    }
                };

                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Validation method returned: isValid={dto.IsValid}, placeholders={dto.Placeholders?.Count ?? 0}");

                // Only a genuine verdict is worth remembering; "couldn't check" must be tried again.
                if (log != null && failureReason == null)
                {
                    await log.StoreAsync(new FieldValidationRunLog
                    {
                        RuleId = rule.Id,
                        RuleHash = ruleHash!,
                        SubjectType = subject!.Type,
                        SubjectId = subject.Id,
                        InputHash = inputHash!,
                        DependencyKey = ValidationInputHasher.DependencyKey(rule, formDependencyValue),
                        IsValid = dto.IsValid,
                        IsBlocking = dto.IsBlocking,
                        Message = dto.Message,
                        PlaceholdersJson = dto.Placeholders == null ? null : JsonSerializer.Serialize(dto.Placeholders),
                        ExecutedAt = DateTime.UtcNow
                    });
                }
                return dto;
            }
            catch (RegionServiceUnavailableException ex)
            {
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Region service unavailable: {ex.Message}");
                return new ValidationResultDto
                {
                    IsValid = false,
                    IsBlocking = rule.IsBlocking,
                    Message = $"Cannot verify this rule right now: {ex.Message}",
                    Metadata = new ValidationMetadataDto
                    {
                        ValidationType = rule.ValidationType,
                        ExecutedAt = DateTime.UtcNow.ToString("o"),
                        FailureReason = "PluginUnreachable"
                    }
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VALIDATION_TRACE_BACKEND]       Exception: {ex.Message}");
                return new ValidationResultDto
                {
                    IsValid = false,
                    IsBlocking = rule.IsBlocking,
                    Message = $"Validation error: {ex.Message}",
                    Metadata = new ValidationMetadataDto
                    {
                        ValidationType = rule.ValidationType,
                        ExecutedAt = DateTime.UtcNow.ToString("o")
                    }
                };
            }
        }

        /// <summary>KNG-269: a stored result of a validator that reads external state is current only within the TTL.</summary>
        private bool Expired(FieldValidationRunLog stored, IValidationMethod validationMethod) =>
            validationMethod.ReadsExternalState
            && _logOptions.ExternalStateTtlHours > 0
            && stored.ExecutedAt < DateTime.UtcNow.AddHours(-_logOptions.ExternalStateTtlHours);

        private static ValidationResultDto FromLog(FieldValidationRunLog stored, FieldValidationRule rule, object? formDependencyValue) => new()
        {
            IsValid = stored.IsValid,
            IsBlocking = stored.IsBlocking,
            Message = stored.Message,
            Placeholders = stored.PlaceholdersJson == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(stored.PlaceholdersJson),
            Metadata = new ValidationMetadataDto
            {
                ValidationType = rule.ValidationType,
                ExecutedAt = DateTime.SpecifyKind(stored.ExecutedAt, DateTimeKind.Utc).ToString("o"),
                DependencyFieldName = rule.DependsOnField?.FieldName,
                DependencyValue = formDependencyValue,
                FromCache = true
            }
        };
    }
}
