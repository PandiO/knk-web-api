using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// See <see cref="ISavedEntityRuleValidator"/>. The form values the wizard would hold are rebuilt
    /// from the saved entity: each form field's property, where an Object field's foreign key
    /// ("TownId") is replaced by its loaded navigation ("Town") - the wizard holds the selected record
    /// there too, and validators read properties off it (Town.WgRegionId). The rules themselves run
    /// through <see cref="IValidationService"/>, the endpoint the wizard calls, so a rule behaves the
    /// same in both places (blocking/non-blocking, "dependency not filled yet", unreachable plugin).
    ///
    /// Scoped: the configuration and rules of each entity type are loaded once per request.
    /// </summary>
    public class SavedEntityRuleValidator : ISavedEntityRuleValidator
    {
        private const BindingFlags PropertyFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
        private static readonly Regex PlaceholderToken = new(@"\{([A-Za-z0-9_.]+)\}", RegexOptions.Compiled);

        private readonly IFormConfigurationRepository _configRepository;
        private readonly IFieldValidationRuleRepository _ruleRepository;
        private readonly IValidationService _validationService;
        private readonly Dictionary<string, FormRules> _formRulesByEntityType = new(StringComparer.OrdinalIgnoreCase);

        public SavedEntityRuleValidator(
            IFormConfigurationRepository configRepository,
            IFieldValidationRuleRepository ruleRepository,
            IValidationService validationService)
        {
            _configRepository = configRepository;
            _ruleRepository = ruleRepository;
            _validationService = validationService;
        }

        public async Task<SavedEntityRuleValidation> ValidateAsync(
            string entityTypeName,
            object entity,
            IReadOnlyDictionary<string, object?>? parentContext = null)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (parentContext != null)
                foreach (var (key, value) in parentContext)
                    context[key] = value;

            var form = await GetFormRulesAsync(entityTypeName);
            var ownValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in form.Fields)
            {
                // Own values win over the parents', including an empty own value.
                var value = FieldValue(entity, field.FieldName);
                ownValues[field.FieldName] = value;
                context[field.FieldName] = value;
            }

            var validation = new SavedEntityRuleValidation { Context = context };
            if (form.RuleFieldIds.Count == 0) return validation;

            var formContextData = new Dictionary<string, object>(context!, StringComparer.OrdinalIgnoreCase);
            foreach (var field in form.Fields.Where(f => form.RuleFieldIds.Contains(f.Id)))
            {
                var value = ownValues[field.FieldName];
                // Cross-field rules check a value against another field; whether a value is needed at
                // all is the field's Required flag (validators treat an empty value as a failure).
                if (value == null || value is string text && string.IsNullOrWhiteSpace(text)) continue;

                var result = await _validationService.ValidateFieldAsync(field.Id, value, null, formContextData);
                validation.Results.Add(new SavedFieldRuleResult
                {
                    FormFieldId = field.Id,
                    FieldName = field.FieldName,
                    FieldLabel = string.IsNullOrWhiteSpace(field.Label) ? field.FieldName : field.Label,
                    Result = result,
                    Message = Interpolate(result.Message, result.Placeholders, context)
                });
            }

            return validation;
        }

        private async Task<FormRules> GetFormRulesAsync(string entityTypeName)
        {
            if (_formRulesByEntityType.TryGetValue(entityTypeName, out var cached)) return cached;

            // The default configuration is the one the wizard opens for this type (ChildFormModal too).
            var config = await _configRepository.GetDefaultByEntityTypeNameAsync(entityTypeName);
            var formRules = new FormRules();
            if (config != null)
            {
                formRules.Fields = config.Steps
                    .Where(s => !s.IsManyToManyRelationship)
                    .SelectMany(s => s.Fields)
                    .Where(f => f.FieldType != FieldType.List && !string.IsNullOrWhiteSpace(f.FieldName))
                    .ToList();
                var rules = await _ruleRepository.GetByFormConfigurationIdAsync(config.Id);
                formRules.RuleFieldIds = rules.Select(r => r.FormFieldId).ToHashSet();
            }

            _formRulesByEntityType[entityTypeName] = formRules;
            return formRules;
        }

        private static object? FieldValue(object entity, string fieldName)
        {
            var type = entity.GetType();
            var property = type.GetProperty(fieldName, PropertyFlags);
            if (property == null) return null;

            var value = property.GetValue(entity);
            if (value != null && fieldName.Length > 2 && fieldName.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
            {
                var navigation = type.GetProperty(fieldName[..^2], PropertyFlags);
                if (navigation?.GetValue(entity) is { } related && related is not IEnumerable) return related;
            }
            return value;
        }

        /// <summary>
        /// Fills {name} from the validator's placeholders, else from the form values ({TownId} or
        /// {Town.Name} - a first segment also matches its "Id" field, which holds the related record).
        /// Unknown placeholders are left as they are, like the wizard does.
        /// </summary>
        internal static string Interpolate(string? message, IReadOnlyDictionary<string, string>? placeholders, IReadOnlyDictionary<string, object?> context)
        {
            if (string.IsNullOrEmpty(message)) return string.Empty;
            return PlaceholderToken.Replace(message, match =>
            {
                var key = match.Groups[1].Value;
                var fromValidator = placeholders?.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (fromValidator?.Key != null) return fromValidator.Value.Value;
                return ResolveFromContext(key, context) ?? match.Value;
            });
        }

        private static string? ResolveFromContext(string path, IReadOnlyDictionary<string, object?> context)
        {
            var segments = path.Split('.');
            if (!TryGet(context, segments[0], out var current) && !TryGet(context, segments[0] + "Id", out current)) return null;

            foreach (var segment in segments.Skip(1))
            {
                if (current == null) return null;
                current = current.GetType().GetProperty(segment, PropertyFlags)?.GetValue(current);
            }

            return current switch
            {
                null => null,
                string s => s,
                IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ when current.GetType().IsPrimitive || current is Enum => current.ToString(),
                _ => null
            };
        }

        private static bool TryGet(IReadOnlyDictionary<string, object?> context, string key, out object? value)
        {
            foreach (var (k, v) in context)
            {
                if (!k.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
                value = v;
                return true;
            }
            value = null;
            return false;
        }

        private sealed class FormRules
        {
            public List<FormField> Fields { get; set; } = new();
            public HashSet<int> RuleFieldIds { get; set; } = new();
        }
    }
}
