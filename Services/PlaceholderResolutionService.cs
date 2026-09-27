using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Services;

/// <summary>
/// Service implementation for resolving placeholder variables in validation error/success messages.
/// Implements multi-layer resolution strategy optimized for single database roundtrip.
/// </summary>
public class PlaceholderResolutionService : IPlaceholderResolutionService
{
    private readonly KnKDbContext _dbContext;
    private readonly IFieldValidationRuleRepository _validationRuleRepository;
    private readonly ILogger<PlaceholderResolutionService> _logger;

    // Regex pattern for extracting placeholders: matches {anything}
    private static readonly Regex PlaceholderPattern = new Regex(@"\{([^}]+)\}", RegexOptions.Compiled);

    public PlaceholderResolutionService(
        KnKDbContext dbContext,
        IFieldValidationRuleRepository validationRuleRepository,
        ILogger<PlaceholderResolutionService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _validationRuleRepository = validationRuleRepository ?? throw new ArgumentNullException(nameof(validationRuleRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<List<string>> ExtractPlaceholdersAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return new List<string>();
        }

        var matches = PlaceholderPattern.Matches(message);
        var placeholders = new List<string>();

        foreach (Match match in matches)
        {
            if (match.Groups.Count > 1)
            {
                placeholders.Add(match.Groups[1].Value);
            }
        }

        _logger.LogDebug("Extracted {Count} placeholders from message: {Message}", placeholders.Count, message);
        return await Task.FromResult(placeholders);
    }

    /// <inheritdoc/>
    public async Task<PlaceholderResolutionResponse> ResolveAllLayersAsync(PlaceholderResolutionRequest request)
    {
        _logger.LogInformation("Starting placeholder resolution for request");

        var response = new PlaceholderResolutionResponse();
        var allPlaceholders = new List<string>();

        try
        {
            // Step 1: Extract placeholder paths
            if (request.FieldValidationRuleId.HasValue)
            {
                // Load the validation rule to extract placeholders from its messages
                var rule = await _validationRuleRepository.GetByIdAsync(request.FieldValidationRuleId.Value);
                if (rule == null)
                {
                    response.ResolutionErrors.Add(new PlaceholderResolutionError
                    {
                        PlaceholderPath = "*",
                        ErrorCode = "RuleNotFound",
                        Message = $"FieldValidationRule with ID {request.FieldValidationRuleId} not found."
                    });
                    return response;
                }

                // Extract from error message
                if (!string.IsNullOrWhiteSpace(rule.ErrorMessage))
                {
                    allPlaceholders.AddRange(await ExtractPlaceholdersAsync(rule.ErrorMessage));
                }

                // Extract from success message
                if (!string.IsNullOrWhiteSpace(rule.SuccessMessage))
                {
                    allPlaceholders.AddRange(await ExtractPlaceholdersAsync(rule.SuccessMessage));
                }
            }

            // Add explicit placeholder paths if provided
            if (request.PlaceholderPaths != null && request.PlaceholderPaths.Any())
            {
                allPlaceholders.AddRange(request.PlaceholderPaths);
            }

            // Remove duplicates
            allPlaceholders = allPlaceholders.Distinct().ToList();
            response.TotalPlaceholdersRequested = allPlaceholders.Count;

            _logger.LogDebug("Total unique placeholders to resolve: {Count}", allPlaceholders.Count);

            // Step 2: Categorize placeholders by layer
            var layer0Placeholders = new List<string>();
            var layer1Placeholders = new List<string>();
            var layer2Placeholders = new List<string>();
            var layer3Placeholders = new List<string>();

            foreach (var placeholderPath in allPlaceholders)
            {
                try
                {
                    var parsed = PlaceholderPath.Parse(placeholderPath);

                    if (parsed.IsAggregateOperation)
                    {
                        layer3Placeholders.Add(placeholderPath);
                    }
                    else if (parsed.Depth == 0)
                    {
                        layer0Placeholders.Add(placeholderPath);
                    }
                    else if (parsed.Depth == 1)
                    {
                        layer1Placeholders.Add(placeholderPath);
                    }
                    else // Depth >= 2
                    {
                        layer2Placeholders.Add(placeholderPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse placeholder path: {Path}", placeholderPath);
                    response.ResolutionErrors.Add(new PlaceholderResolutionError
                    {
                        PlaceholderPath = placeholderPath,
                        ErrorCode = "InvalidPath",
                        Message = $"Invalid placeholder path format: {placeholderPath}",
                        StackTrace = ex.StackTrace
                    });
                }
            }

            // Step 3: Resolve each layer
            _logger.LogDebug("Layer distribution - L0: {L0}, L1: {L1}, L2: {L2}, L3: {L3}",
                layer0Placeholders.Count, layer1Placeholders.Count, layer2Placeholders.Count, layer3Placeholders.Count);

            // Layer 0: Direct form field values
            var layer0Results = await ResolveLayer0Async(request.CurrentEntityPlaceholders);
            foreach (var kvp in layer0Results)
            {
                response.ResolvedPlaceholders[kvp.Key] = kvp.Value;
            }

            // Layers 1-3: Require entity type and ID
            if (request.EntityId.HasValue && !string.IsNullOrWhiteSpace(request.EntityTypeName))
            {
                var entityType = GetEntityType(request.EntityTypeName);
                if (entityType == null)
                {
                    _logger.LogWarning("Entity type not found: {TypeName}", request.EntityTypeName);
                    response.ResolutionErrors.Add(new PlaceholderResolutionError
                    {
                        PlaceholderPath = "*",
                        ErrorCode = "EntityTypeNotFound",
                        Message = $"Entity type '{request.EntityTypeName}' not found."
                    });
                }
                else
                {
                    // Layer 1: Single navigation
                    if (layer1Placeholders.Any())
                    {
                        var layer1Results = await ResolveLayer1Async(entityType, request.EntityId.Value, layer1Placeholders);
                        foreach (var kvp in layer1Results)
                        {
                            response.ResolvedPlaceholders[kvp.Key] = kvp.Value;
                        }
                    }

                    // Layer 2: Multi-level navigation
                    if (layer2Placeholders.Any())
                    {
                        var layer2Results = await ResolveLayer2Async(entityType, request.EntityId.Value, layer2Placeholders);
                        foreach (var kvp in layer2Results)
                        {
                            response.ResolvedPlaceholders[kvp.Key] = kvp.Value;
                        }
                    }

                    // Layer 3: Aggregates
                    if (layer3Placeholders.Any())
                    {
                        var layer3Results = await ResolveLayer3Async(entityType, request.EntityId.Value, layer3Placeholders);
                        foreach (var kvp in layer3Results)
                        {
                            response.ResolvedPlaceholders[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            else if (layer1Placeholders.Any() || layer2Placeholders.Any() || layer3Placeholders.Any())
            {
                _logger.LogWarning("Layers 1-3 placeholders found but EntityId or EntityTypeName not provided");
                var skipMessage = "EntityId and EntityTypeName required for navigation placeholders";
                foreach (var path in layer1Placeholders.Concat(layer2Placeholders).Concat(layer3Placeholders))
                {
                    response.ResolutionErrors.Add(new PlaceholderResolutionError
                    {
                        PlaceholderPath = path,
                        ErrorCode = "DependencyNotFilled",
                        Message = skipMessage
                    });
                }
            }

            _logger.LogInformation("Placeholder resolution complete. Resolved: {Resolved}, Errors: {Errors}",
                response.ResolvedPlaceholders.Count, response.ResolutionErrors.Count);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during placeholder resolution");
            response.ResolutionErrors.Add(new PlaceholderResolutionError
            {
                PlaceholderPath = "*",
                ErrorCode = "Exception",
                Message = "Unexpected error during placeholder resolution",
                StackTrace = ex.StackTrace
            });
            return response;
        }
    }

    /// <inheritdoc/>
    public Task<Dictionary<string, string>> ResolveLayer0Async(Dictionary<string, string>? currentEntityPlaceholders)
    {
        _logger.LogDebug("Resolving Layer 0 placeholders");

        if (currentEntityPlaceholders == null || !currentEntityPlaceholders.Any())
        {
            return Task.FromResult(new Dictionary<string, string>());
        }

        // Validate: Layer 0 keys must not contain dots
        var invalidKeys = currentEntityPlaceholders.Keys.Where(k => k.Contains('.')).ToList();
        if (invalidKeys.Any())
        {
            _logger.LogWarning("Layer 0 placeholders contain navigation dots (should be Layer 1+): {Keys}",
                string.Join(", ", invalidKeys));
        }

        // Return as-is (already stringified by frontend)
        _logger.LogDebug("Resolved {Count} Layer 0 placeholders", currentEntityPlaceholders.Count);
        return Task.FromResult(new Dictionary<string, string>(currentEntityPlaceholders));
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, string>> ResolveLayer1Async(
        Type entityType,
        object entityId,
        List<string> singleNavPlaceholders)
    {
        _logger.LogDebug("Resolving {Count} Layer 1 placeholders for entity {Type} ID {Id}",
            singleNavPlaceholders.Count, entityType.Name, entityId);

        var results = new Dictionary<string, string>();

        // Get the entity from database
        var entity = await GetEntityByIdAsync(entityType, entityId);
        if (entity == null)
        {
            _logger.LogWarning("Entity {Type} with ID {Id} not found", entityType.Name, entityId);
            return results;
        }

        // Group by navigation property
        var groupedByNav = singleNavPlaceholders
            .Select(p => PlaceholderPath.Parse(p))
            .GroupBy(p => p.Segments[0]);

        foreach (var navGroup in groupedByNav)
        {
            var navPropertyName = navGroup.Key;

            try
            {
                // Get navigation property info
                var navProperty = entityType.GetProperty(navPropertyName);
                if (navProperty == null)
                {
                    _logger.LogWarning("Navigation property {Property} not found on {Type}", navPropertyName, entityType.Name);
                    continue;
                }

                // Get foreign key value
                var foreignKeyProperty = GetForeignKeyProperty(entityType, navPropertyName);
                if (foreignKeyProperty == null)
                {
                    _logger.LogWarning("Foreign key for navigation {Property} not found on {Type}", navPropertyName, entityType.Name);
                    continue;
                }

                var foreignKeyValue = foreignKeyProperty.GetValue(entity);
                if (foreignKeyValue == null)
                {
                    _logger.LogDebug("Foreign key {FK} is null for navigation {Property}", foreignKeyProperty.Name, navPropertyName);
                    continue;
                }

                // Fetch related entity
                var navEntityType = navProperty.PropertyType;
                var relatedEntity = await GetEntityByIdAsync(navEntityType, foreignKeyValue);

                if (relatedEntity == null)
                {
                    _logger.LogWarning("Related entity {Type} with ID {Id} not found", navEntityType.Name, foreignKeyValue);
                    continue;
                }

                // Extract requested properties
                foreach (var placeholder in navGroup)
                {
                    var propertyName = placeholder.FinalSegment;
                    var property = navEntityType.GetProperty(propertyName);

                    if (property == null)
                    {
                        _logger.LogWarning("Property {Property} not found on {Type}", propertyName, navEntityType.Name);
                        continue;
                    }

                    var value = property.GetValue(relatedEntity);
                    results[placeholder.FullPath] = value?.ToString() ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving Layer 1 navigation {Nav}", navPropertyName);
            }
        }

        _logger.LogDebug("Resolved {Count} Layer 1 placeholders", results.Count);
        return results;
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, string>> ResolveLayer2Async(
        Type entityType,
        object entityId,
        List<string> multiNavPlaceholders)
    {
        _logger.LogDebug("Resolving {Count} Layer 2 placeholders for entity {Type} ID {Id}",
            multiNavPlaceholders.Count, entityType.Name, entityId);

        var results = new Dictionary<string, string>();

        try
        {
            // Parse all placeholder paths
            var parsedPaths = multiNavPlaceholders.Select(p => PlaceholderPath.Parse(p)).ToList();

            // Build all required Include paths
            var includePaths = parsedPaths
                .SelectMany(p => p.GetIncludePaths())
                .Distinct()
                .ToList();

            _logger.LogDebug("Building query with {Count} Include paths: {Paths}",
                includePaths.Count, string.Join(", ", includePaths));

            // Fetch entity with all includes in single query
            var entity = await GetEntityWithIncludesAsync(entityType, entityId, includePaths);

            if (entity == null)
            {
                _logger.LogWarning("Entity {Type} with ID {Id} not found", entityType.Name, entityId);
                return results;
            }

            // Navigate each placeholder path
            foreach (var placeholder in parsedPaths)
            {
                try
                {
                    // Navigate the chain
                    object? currentValue = entity;
                    foreach (var segment in placeholder.GetNavigationChain())
                    {
                        if (currentValue == null) break;

                        var property = currentValue.GetType().GetProperty(segment);
                        if (property == null)
                        {
                            _logger.LogWarning("Property {Property} not found during navigation of {Path}",
                                segment, placeholder.FullPath);
                            currentValue = null;
                            break;
                        }

                        currentValue = property.GetValue(currentValue);
                    }

                    if (currentValue == null)
                    {
                        _logger.LogDebug("Navigation chain resulted in null for {Path}", placeholder.FullPath);
                        continue;
                    }

                    // Extract final property
                    var finalProperty = currentValue.GetType().GetProperty(placeholder.FinalSegment);
                    if (finalProperty == null)
                    {
                        _logger.LogWarning("Final property {Property} not found in {Path}",
                            placeholder.FinalSegment, placeholder.FullPath);
                        continue;
                    }

                    var value = finalProperty.GetValue(currentValue);
                    results[placeholder.FullPath] = value?.ToString() ?? string.Empty;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error navigating placeholder path {Path}", placeholder.FullPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Layer 2 resolution");
        }

        _logger.LogDebug("Resolved {Count} Layer 2 placeholders", results.Count);
        return results;
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, string>> ResolveLayer3Async(
        Type entityType,
        object entityId,
        List<string> aggregatePlaceholders)
    {
        _logger.LogDebug("Resolving {Count} Layer 3 placeholders for entity {Type} ID {Id}",
            aggregatePlaceholders.Count, entityType.Name, entityId);

        var results = new Dictionary<string, string>();

        try
        {
            // Parse all placeholder paths
            var parsedPaths = aggregatePlaceholders.Select(p => PlaceholderPath.Parse(p)).ToList();

            // Build Include paths (excluding the aggregate operation itself)
            var includePaths = parsedPaths
                .Select(p => p.NavigationPath) // e.g., "Town.Districts" without ".Count"
                .Distinct()
                .ToList();

            _logger.LogDebug("Building query with {Count} Include paths for aggregates: {Paths}",
                includePaths.Count, string.Join(", ", includePaths));

            // Fetch entity with includes
            var entity = await GetEntityWithIncludesAsync(entityType, entityId, includePaths);

            if (entity == null)
            {
                _logger.LogWarning("Entity {Type} with ID {Id} not found", entityType.Name, entityId);
                return results;
            }

            // Process each aggregate
            foreach (var placeholder in parsedPaths)
            {
                try
                {
                    // Navigate to the collection
                    object? currentValue = entity;
                    var navChain = placeholder.NavigationPath.Split('.', StringSplitOptions.RemoveEmptyEntries);

                    foreach (var segment in navChain)
                    {
                        if (currentValue == null) break;

                        var property = currentValue.GetType().GetProperty(segment);
                        if (property == null)
                        {
                            _logger.LogWarning("Property {Property} not found during aggregate navigation of {Path}",
                                segment, placeholder.FullPath);
                            currentValue = null;
                            break;
                        }

                        currentValue = property.GetValue(currentValue);
                    }

                    if (currentValue == null)
                    {
                        _logger.LogDebug("Navigation resulted in null for aggregate {Path}", placeholder.FullPath);
                        continue;
                    }

                    // Execute aggregate operation
                    var aggregateOp = placeholder.AggregateOperationName?.ToLower() ?? "count";
                    string? result = null;

                    if (currentValue is IEnumerable enumerable)
                    {
                        result = aggregateOp switch
                        {
                            "count" => enumerable.Cast<object>().Count().ToString(),
                            "any" => enumerable.Cast<object>().Any().ToString().ToLower(),
                            "first" => enumerable.Cast<object>().FirstOrDefault()?.ToString() ?? string.Empty,
                            "last" => enumerable.Cast<object>().LastOrDefault()?.ToString() ?? string.Empty,
                            _ => $"[Unknown aggregate: {aggregateOp}]"
                        };
                    }
                    else
                    {
                        _logger.LogWarning("Navigation result is not a collection for aggregate {Path}", placeholder.FullPath);
                        continue;
                    }

                    results[placeholder.FullPath] = result ?? string.Empty;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing aggregate for {Path}", placeholder.FullPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Layer 3 resolution");
        }

        _logger.LogDebug("Resolved {Count} Layer 3 placeholders", results.Count);
        return results;
    }

    /// <inheritdoc/>
    public string InterpolatePlaceholders(string messageTemplate, Dictionary<string, string> placeholders)
    {
        if (string.IsNullOrWhiteSpace(messageTemplate))
        {
            return string.Empty;
        }

        if (placeholders == null || !placeholders.Any())
        {
            return messageTemplate;
        }

        var result = messageTemplate;
        foreach (var kvp in placeholders)
        {
            var pattern = $"{{{kvp.Key}}}";
            result = result.Replace(pattern, kvp.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<object?> LoadEntityByIdAsync(string entityTypeName, object id)
    {
        var entityType = GetEntityType(entityTypeName);
        var keyType = entityType == null
            ? null
            : _dbContext.Model.FindEntityType(entityType)?.FindPrimaryKey()?.Properties.SingleOrDefault()?.ClrType;
        if (entityType == null || keyType == null) return null;

        object key;
        try
        {
            // FindAsync needs the key's exact CLR type (an int key rejects a long from JSON).
            key = Convert.ChangeType(id, Nullable.GetUnderlyingType(keyType) ?? keyType, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            return null;
        }

        return await GetEntityByIdAsync(entityType, key);
    }

    #region Helper Methods

    /// <summary>
    /// Get entity type by name from the DbContext model.
    /// </summary>
    private Type? GetEntityType(string entityTypeName)
    {
        var entityTypes = _dbContext.Model.GetEntityTypes();
        var entityType = entityTypes.FirstOrDefault(t =>
            t.ClrType.Name.Equals(entityTypeName, StringComparison.OrdinalIgnoreCase));

        return entityType?.ClrType;
    }

    /// <summary>
    /// Get entity by ID using reflection and DbContext.Set&lt;T&gt;().FindAsync().
    /// </summary>
    private async Task<object?> GetEntityByIdAsync(Type entityType, object entityId)
    {
        var setMethod = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes);
        if (setMethod == null) return null;

        var genericSet = setMethod.MakeGenericMethod(entityType);
        var dbSet = genericSet.Invoke(_dbContext, null);

        if (dbSet == null) return null;

        var findMethod = dbSet.GetType().GetMethod(nameof(DbSet<object>.FindAsync), new[] { typeof(object[]) });
        if (findMethod == null) return null;

        var findTask = findMethod.Invoke(dbSet, new object[] { new[] { entityId } });
        if (findTask == null) return null;

        // Await the ValueTask<T>
        var valueTaskType = findTask.GetType();
        var asTaskMethod = valueTaskType.GetMethod(nameof(ValueTask<object>.AsTask));
        if (asTaskMethod == null) return null;

        var task = (Task?)asTaskMethod.Invoke(findTask, null);
        if (task == null) return null;

        await task.ConfigureAwait(false);

        var resultProperty = task.GetType().GetProperty(nameof(Task<object>.Result));
        return resultProperty?.GetValue(task);
    }

    /// <summary>
    /// Get entity with specified Include paths in a single query.
    /// </summary>
    private async Task<object?> GetEntityWithIncludesAsync(Type entityType, object entityId, List<string> includePaths)
    {
        var setMethod = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes);
        if (setMethod == null) return null;

        var genericSet = setMethod.MakeGenericMethod(entityType);
        var queryable = genericSet.Invoke(_dbContext, null) as IQueryable;

        if (queryable == null) return null;

        // Apply Include paths
        foreach (var includePath in includePaths)
        {
            var includeMethod = typeof(EntityFrameworkQueryableExtensions)
                .GetMethods()
                .FirstOrDefault(m =>
                    m.Name == nameof(EntityFrameworkQueryableExtensions.Include) &&
                    m.GetParameters().Length == 2 &&
                    m.GetParameters()[1].ParameterType == typeof(string));

            if (includeMethod != null)
            {
                var genericInclude = includeMethod.MakeGenericMethod(entityType);
                queryable = genericInclude.Invoke(null, new object[] { queryable, includePath }) as IQueryable;
            }
        }

        if (queryable == null) return null;

        // Execute query: FirstOrDefaultAsync(e => e.Id == entityId)
        var parameter = System.Linq.Expressions.Expression.Parameter(entityType, "e");
        var idProperty = entityType.GetProperty("Id");
        if (idProperty == null) return null;

        var idAccess = System.Linq.Expressions.Expression.Property(parameter, idProperty);
        var idConstant = System.Linq.Expressions.Expression.Constant(entityId);
        var equals = System.Linq.Expressions.Expression.Equal(idAccess, idConstant);
        var lambda = System.Linq.Expressions.Expression.Lambda(equals, parameter);

        var whereMethod = typeof(Queryable)
            .GetMethods()
            .FirstOrDefault(m => m.Name == nameof(Queryable.Where) && m.GetParameters().Length == 2);

        if (whereMethod != null)
        {
            var genericWhere = whereMethod.MakeGenericMethod(entityType);
            queryable = genericWhere.Invoke(null, new object[] { queryable, lambda }) as IQueryable;
        }

        if (queryable == null) return null;

        var firstOrDefaultMethod = typeof(EntityFrameworkQueryableExtensions)
            .GetMethods()
            .FirstOrDefault(m =>
                m.Name == nameof(EntityFrameworkQueryableExtensions.FirstOrDefaultAsync) &&
                m.GetParameters().Length == 2); // IQueryable<T>, CancellationToken

        if (firstOrDefaultMethod != null)
        {
            var genericFirst = firstOrDefaultMethod.MakeGenericMethod(entityType);
            var task = genericFirst.Invoke(null, new object[] { queryable, default(System.Threading.CancellationToken) }) as Task;

            if (task != null)
            {
                await task.ConfigureAwait(false);
                var resultProperty = task.GetType().GetProperty(nameof(Task<object>.Result));
                return resultProperty?.GetValue(task);
            }
        }

        return null;
    }

    /// <summary>
    /// Get foreign key property for a navigation property.
    /// Simple heuristic: looks for property named "{NavigationPropertyName}Id".
    /// </summary>
    private PropertyInfo? GetForeignKeyProperty(Type entityType, string navigationPropertyName)
    {
        var conventionalFkName = $"{navigationPropertyName}Id";
        return entityType.GetProperty(conventionalFkName);
    }

    #endregion
}
