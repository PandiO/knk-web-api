using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.ValidationMethods;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Services;

/// <summary>
/// Service implementation for executing field validations with placeholder resolution.
/// Coordinates validation logic execution and placeholder variable resolution.
/// </summary>
public class FieldValidationService : IFieldValidationService
{
    private readonly IPlaceholderResolutionService _placeholderService;
    private readonly ILogger<FieldValidationService> _logger;
    private readonly Dictionary<string, IValidationMethod> _validationMethods;

    public FieldValidationService(
        IPlaceholderResolutionService placeholderService,
        IEnumerable<IValidationMethod> validationMethods,
        ILogger<FieldValidationService> logger)
    {
        _placeholderService = placeholderService ?? throw new ArgumentNullException(nameof(placeholderService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Build lookup dictionary by validation type
        _validationMethods = validationMethods?.ToDictionary(v => v.ValidationType, v => v)
            ?? throw new ArgumentNullException(nameof(validationMethods));
    }

    /// <inheritdoc/>
    public async Task<ValidationResultDto> ValidateFieldAsync(
        FieldValidationRule rule,
        object? fieldValue,
        object? dependencyFieldValue = null,
        Dictionary<string, string>? currentEntityPlaceholders = null,
        int? entityId = null)
    {
        if (rule == null)
        {
            throw new ArgumentNullException(nameof(rule));
        }

        _logger.LogInformation("Executing validation for rule {RuleId} (Type: {ValidationType})",
            rule.Id, rule.ValidationType);

        try
        {
            // Step 1: Resolve placeholders for this rule
            var placeholderResponse = await ResolvePlaceholdersForRuleAsync(rule, entityId, currentEntityPlaceholders);

            // Check if placeholder resolution had critical errors
            if (placeholderResponse.ResolutionErrors.Any(e => e.ErrorCode == "RuleNotFound" || e.ErrorCode == "EntityTypeNotFound"))
            {
                _logger.LogError("Critical placeholder resolution error for rule {RuleId}", rule.Id);
                return new ValidationResultDto
                {
                    IsValid = false,
                    IsBlocking = rule.IsBlocking,
                    Message = "Validation configuration error. Please contact administrator.",
                    Placeholders = new Dictionary<string, string>(),
                    Metadata = new ValidationMetadataDto
                    {
                        ValidationType = rule.ValidationType,
                        ExecutedAt = DateTime.UtcNow.ToString("o")
                    }
                };
            }

            var placeholders = placeholderResponse.ResolvedPlaceholders;

            // Step 2: Dispatch to type-specific validation method
            ValidationResultDto result = rule.ValidationType switch
            {
                "LocationInsideRegion" => await ValidateLocationInsideRegionAsync(rule, fieldValue, dependencyFieldValue, placeholders),
                "RegionContainment" => await ValidateRegionContainmentAsync(rule, fieldValue, dependencyFieldValue, placeholders),
                "ConditionalRequired" => await ValidateConditionalRequiredAsync(rule, fieldValue, dependencyFieldValue, placeholders),
                _ => new ValidationResultDto
                {
                    IsValid = false,
                    IsBlocking = false,
                    Message = $"Unknown validation type: {rule.ValidationType}",
                    Placeholders = placeholders,
                    Metadata = new ValidationMetadataDto
                    {
                        ValidationType = rule.ValidationType,
                        ExecutedAt = DateTime.UtcNow.ToString("o")
                    }
                }
            };

            // Step 3: Add metadata
            result.Metadata = new ValidationMetadataDto
            {
                ValidationType = rule.ValidationType,
                ExecutedAt = DateTime.UtcNow.ToString("o"),
                DependencyValue = dependencyFieldValue
            };

            _logger.LogInformation("Validation complete for rule {RuleId}. Result: {IsValid}",
                rule.Id, result.IsValid);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during validation for rule {RuleId}", rule.Id);
            return new ValidationResultDto
            {
                IsValid = false,
                IsBlocking = rule.IsBlocking,
                Message = "Validation error occurred. Please try again.",
                Placeholders = new Dictionary<string, string>(),
                Metadata = new ValidationMetadataDto
                {
                    ValidationType = rule.ValidationType,
                    ExecutedAt = DateTime.UtcNow.ToString("o")
                }
            };
        }
    }

    /// <inheritdoc/>
    public async Task<PlaceholderResolutionResponse> ResolvePlaceholdersForRuleAsync(
        FieldValidationRule rule,
        int? entityId = null,
        Dictionary<string, string>? currentEntityPlaceholders = null)
    {
        if (rule == null)
        {
            throw new ArgumentNullException(nameof(rule));
        }

        _logger.LogDebug("Resolving placeholders for rule {RuleId}", rule.Id);

        // Build request
        var request = new PlaceholderResolutionRequest
        {
            FieldValidationRuleId = rule.Id,
            EntityId = entityId,
            CurrentEntityPlaceholders = currentEntityPlaceholders ?? new Dictionary<string, string>()
        };

        // Attempt to infer entity type from rule's FormFieldId
        // (This requires FormField -> FormConfiguration -> EntityType lookup)
        // For now, we'll rely on explicit entityTypeName in request or from rule's ConfigJson
        if (!string.IsNullOrWhiteSpace(rule.ConfigJson))
        {
            try
            {
                var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rule.ConfigJson);
                if (config != null && config.TryGetValue("entityTypeName", out var entityTypeValue))
                {
                    request.EntityTypeName = entityTypeValue.GetString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse entityTypeName from rule {RuleId} ConfigJson", rule.Id);
            }
        }

        // Resolve all layers
        return await _placeholderService.ResolveAllLayersAsync(request);
    }

    /// <inheritdoc/>
    public async Task<ValidationResultDto> ValidateLocationInsideRegionAsync(
        FieldValidationRule rule,
        object? fieldValue,
        object? dependencyFieldValue,
        Dictionary<string, string> placeholders)
    {
        _logger.LogDebug("Executing LocationInsideRegion validation for rule {RuleId}", rule.Id);

        // Delegate to registered validator if available
        if (_validationMethods.TryGetValue("LocationInsideRegion", out var validator))
        {
            // Build form context data from placeholders
            var formContextData = placeholders?.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);
            
            var result = await validator.ValidateAsync(fieldValue, dependencyFieldValue, rule.ConfigJson, formContextData);
            var failureReason = result.Metadata != null && result.Metadata.TryGetValue("failureReason", out var reasonObj)
                ? reasonObj?.ToString()
                : null;
            return new ValidationResultDto
            {
                IsValid = result.IsValid,
                IsBlocking = rule.IsBlocking,
                // KNG-173: the rule's message names no place; append what it must be inside.
                Message = result.IsValid
                    ? (rule.SuccessMessage ?? result.Message)
                    : (failureReason != null ? result.Message : RegionRuleMessages.WithParent(rule, rule.ErrorMessage, result.Placeholders)),
                Placeholders = result.Placeholders ?? placeholders,
                Metadata = failureReason != null ? new ValidationMetadataDto { ValidationType = rule.ValidationType, ExecutedAt = DateTime.UtcNow.ToString("o"), FailureReason = failureReason } : null
            };
        }

        _logger.LogWarning("LocationInsideRegion validator not found in dependency injection");
        return new ValidationResultDto
        {
            IsValid = true,
            IsBlocking = rule.IsBlocking,
            Message = rule.SuccessMessage ?? "Location is valid",
            Placeholders = placeholders
        };
    }

    /// <inheritdoc/>
    public async Task<ValidationResultDto> ValidateRegionContainmentAsync(
        FieldValidationRule rule,
        object? fieldValue,
        object? dependencyFieldValue,
        Dictionary<string, string> placeholders)
    {
        _logger.LogDebug("Executing RegionContainment validation for rule {RuleId}", rule.Id);

        // Delegate to registered validator if available
        if (_validationMethods.TryGetValue("RegionContainment", out var validator))
        {
            // Build form context data from placeholders
            var formContextData = placeholders?.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);
            
            var result = await validator.ValidateAsync(fieldValue, dependencyFieldValue, rule.ConfigJson, formContextData);
            var failureReason = result.Metadata != null && result.Metadata.TryGetValue("failureReason", out var reasonObj)
                ? reasonObj?.ToString()
                : null;
            return new ValidationResultDto
            {
                IsValid = result.IsValid,
                IsBlocking = rule.IsBlocking,
                // KNG-173: the rule's message names no place; append what it must be inside.
                Message = result.IsValid
                    ? (rule.SuccessMessage ?? result.Message)
                    : (failureReason != null ? result.Message : RegionRuleMessages.WithParent(rule, rule.ErrorMessage, result.Placeholders)),
                Placeholders = result.Placeholders ?? placeholders,
                Metadata = failureReason != null ? new ValidationMetadataDto { ValidationType = rule.ValidationType, ExecutedAt = DateTime.UtcNow.ToString("o"), FailureReason = failureReason } : null
            };
        }

        _logger.LogWarning("RegionContainment validator not found in dependency injection");
        return new ValidationResultDto
        {
            IsValid = true,
            IsBlocking = rule.IsBlocking,
            Message = rule.SuccessMessage ?? "Region is valid",
            Placeholders = placeholders
        };
    }

    /// <inheritdoc/>
    public async Task<ValidationResultDto> ValidateConditionalRequiredAsync(
        FieldValidationRule rule,
        object? fieldValue,
        object? dependencyFieldValue,
        Dictionary<string, string> placeholders)
    {
        _logger.LogDebug("Executing ConditionalRequired validation for rule {RuleId}", rule.Id);

        // Delegate to registered validator if available
        if (_validationMethods.TryGetValue("ConditionalRequired", out var validator))
        {
            // Build form context data from placeholders
            var formContextData = placeholders?.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);
            
            var result = await validator.ValidateAsync(fieldValue, dependencyFieldValue, rule.ConfigJson, formContextData);
            return new ValidationResultDto
            {
                IsValid = result.IsValid,
                IsBlocking = rule.IsBlocking,
                Message = result.IsValid ? (rule.SuccessMessage ?? result.Message) : rule.ErrorMessage,
                Placeholders = result.Placeholders ?? placeholders
            };
        }

        _logger.LogWarning("ConditionalRequired validator not found in dependency injection");
        return new ValidationResultDto
        {
            IsValid = true,
            IsBlocking = rule.IsBlocking,
            Message = rule.SuccessMessage ?? "Field is valid",
            Placeholders = placeholders
        };
    }
}
