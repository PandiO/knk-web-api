using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// PandiO/knk-workspace#289: names the form fields and form steps that still use a form configuration (as a sub-form),
    /// for "Show what uses it" on Entity setup. A row is id + label + one detail line that says which form (and step) it
    /// sits in, so the admin knows where to change it: "Town" / "Field of the form "Structure form" (Structure), step
    /// "Location"". Nothing else of the row is returned.
    /// </summary>
    public interface IConfigurationReferenceLabelService
    {
        /// <summary>Whether rows of this type are named here (FormField, FormStep).</summary>
        bool Handles(string? typeName);

        /// <summary>One item per id, in the order given; an id that isn't found keeps "&lt;Type&gt; #id".</summary>
        Task<List<LookupItemDto>> LabelAsync(string typeName, IReadOnlyList<int> ids);
    }

    public class ConfigurationReferenceLabelService : IConfigurationReferenceLabelService
    {
        private readonly KnKDbContext _context;

        public ConfigurationReferenceLabelService(KnKDbContext context)
        {
            _context = context;
        }

        public bool Handles(string? typeName) => typeName is nameof(FormField) or nameof(FormStep);

        public async Task<List<LookupItemDto>> LabelAsync(string typeName, IReadOnlyList<int> ids)
        {
            var found = typeName switch
            {
                nameof(FormField) => await FieldLabelsAsync(ids),
                nameof(FormStep) => await StepLabelsAsync(ids),
                _ => new Dictionary<int, LookupItemDto>()
            };
            return ids
                .Select(id => found.TryGetValue(id, out var item) ? item : new LookupItemDto { Id = id, Label = $"{typeName} #{id}" })
                .ToList();
        }

        private async Task<Dictionary<int, LookupItemDto>> FieldLabelsAsync(IReadOnlyList<int> ids)
        {
            var fields = await _context.FormFields.AsNoTracking()
                .Where(f => ids.Contains(f.Id))
                .Select(f => new
                {
                    f.Id,
                    f.Label,
                    f.FieldName,
                    f.IsReusable,
                    StepName = f.FormStep != null ? f.FormStep.StepName : null,
                    Config = f.FormStep != null ? f.FormStep.FormConfiguration : null,
                    ParentConfig = f.FormStep != null && f.FormStep.ParentStep != null ? f.FormStep.ParentStep.FormConfiguration : null,
                })
                .ToListAsync();
            return fields.ToDictionary(f => f.Id, f =>
            {
                var config = f.Config ?? f.ParentConfig;
                string detail;
                if (config != null)
                {
                    detail = $"Field of the form {FormName(config)}" + (string.IsNullOrWhiteSpace(f.StepName) ? "" : $", step \"{f.StepName}\"");
                }
                else
                {
                    detail = f.IsReusable ? "Reusable field template" : "Field not in a form";
                }
                return new LookupItemDto
                {
                    Id = f.Id,
                    Label = string.IsNullOrWhiteSpace(f.Label) ? f.FieldName : f.Label,
                    Detail = detail,
                };
            });
        }

        private async Task<Dictionary<int, LookupItemDto>> StepLabelsAsync(IReadOnlyList<int> ids)
        {
            var steps = await _context.FormSteps.AsNoTracking()
                .Where(s => ids.Contains(s.Id))
                .Select(s => new
                {
                    s.Id,
                    s.StepName,
                    s.IsReusable,
                    Config = s.FormConfiguration,
                    ParentConfig = s.ParentStep != null ? s.ParentStep.FormConfiguration : null,
                })
                .ToListAsync();
            return steps.ToDictionary(s => s.Id, s =>
            {
                var config = s.Config ?? s.ParentConfig;
                return new LookupItemDto
                {
                    Id = s.Id,
                    Label = string.IsNullOrWhiteSpace(s.StepName) ? $"Step #{s.Id}" : s.StepName,
                    Detail = config != null
                        ? $"Step of the form {FormName(config)}"
                        : s.IsReusable ? "Reusable step template" : "Step not in a form",
                };
            });
        }

        /// <summary>"\"Structure form\" (Structure)", "(Structure, draft)" for a builder draft.</summary>
        private static string FormName(FormConfiguration config) =>
            $"\"{config.Name}\" ({config.EntityTypeName}{(config.IsDraft ? ", draft" : "")})";
    }
}
