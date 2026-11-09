using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// PandiO/knk-workspace#71: the form builder's draft and publish steps.
    /// <list type="bullet">
    /// <item>The rows (steps, fields) are always the <b>published</b> version: the form wizard, "New with", pickers,
    /// child forms and saved drafts keep reading them until Publish.</item>
    /// <item>The builder saves its working copy as <c>DraftJson</c> (no validation: a draft may be incomplete).</item>
    /// <item><b>Publish</b> applies the copy through <see cref="IFormConfigurationService.UpdateAsync"/> (every check
    /// a save ran before #71), moves the default when asked, and clears the copy.</item>
    /// <item>A configuration created in the builder or duplicated is <c>IsDraft</c> (never published) until its first
    /// Publish; the wizard never offers it. Rows from before #71 and imports are published.</item>
    /// </list>
    /// </summary>
    public interface IFormConfigurationDraftService
    {
        Task<FormConfigurationBuilderStateDto?> GetBuilderStateAsync(int id);
        Task<FormConfigurationBuilderStateDto> CreateDraftAsync(FormConfigurationDto draft);
        Task<FormConfigurationBuilderStateDto> SaveDraftAsync(int id, FormConfigurationDto draft);
        Task<FormConfigurationBuilderStateDto> DiscardDraftAsync(int id);
        Task<FormConfigurationDto> PublishAsync(int id);
        Task<FormConfigurationBuilderStateDto> DuplicateAsync(int id, string? name);

        /// <summary>True when the configuration exists and was never published.</summary>
        Task<bool> IsNeverPublishedAsync(int id);

        /// <summary>After an edit outside the builder (Set default on Entity setup), the saved copy follows its default flag.</summary>
        Task SyncDraftDefaultAsync(int id, bool isDefault);
    }

    public class FormConfigurationDraftService : IFormConfigurationDraftService
    {
        private static readonly string[] OpenDraftStatuses = { "InProgress", "Paused" };

        private readonly KnKDbContext _db;
        private readonly IFormConfigurationRepository _repo;
        private readonly IFormConfigurationService _configurations;
        private readonly IFormConfigurationPortabilityService _portability;
        private readonly IMapper _mapper;

        public FormConfigurationDraftService(
            KnKDbContext db,
            IFormConfigurationRepository repo,
            IFormConfigurationService configurations,
            IFormConfigurationPortabilityService portability,
            IMapper mapper)
        {
            _db = db;
            _repo = repo;
            _configurations = configurations;
            _portability = portability;
            _mapper = mapper;
        }

        public async Task<FormConfigurationBuilderStateDto?> GetBuilderStateAsync(int id)
        {
            if (id <= 0) return null;
            var row = await _repo.GetByIdAsync(id);
            return row == null ? null : await BuildStateAsync(row);
        }

        public async Task<FormConfigurationBuilderStateDto> CreateDraftAsync(FormConfigurationDto draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (string.IsNullOrWhiteSpace(draft.EntityTypeName)) throw new ArgumentException("Choose an entity first.");
            var name = RequireName(draft.ConfigurationName);

            draft.Id = null;
            var row = new FormConfiguration
            {
                Name = name,
                EntityTypeName = draft.EntityTypeName.Trim(),
                Description = string.IsNullOrWhiteSpace(draft.Description) ? null : draft.Description,
                IsDefault = false,
                IsDraft = true,
                StepOrderJson = "[]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            draft.EntityTypeName = row.EntityTypeName;
            draft.ConfigurationName = name;
            row.DraftJson = SerializeDraft(draft);
            row.DraftSavedAt = DateTime.UtcNow;

            _db.FormConfigurations.Add(row);
            await _db.SaveChangesAsync();
            return (await GetBuilderStateAsync(row.Id))!;
        }

        public async Task<FormConfigurationBuilderStateDto> SaveDraftAsync(int id, FormConfigurationDto draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            var row = await _db.FormConfigurations.FirstOrDefaultAsync(c => c.Id == id)
                      ?? throw new KeyNotFoundException($"FormConfiguration with id {id} not found.");
            if (!string.Equals(row.EntityTypeName, draft.EntityTypeName?.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"This configuration is for {row.EntityTypeName}; its entity can't change.");
            var name = RequireName(draft.ConfigurationName);

            draft.Id = id.ToString();
            draft.EntityTypeName = row.EntityTypeName;
            draft.ConfigurationName = name;
            row.DraftJson = SerializeDraft(draft);
            row.DraftSavedAt = DateTime.UtcNow;
            if (row.IsDraft)
            {
                // Never published: the list on Entity setup shows the name and description being worked on.
                row.Name = name;
                row.Description = string.IsNullOrWhiteSpace(draft.Description) ? null : draft.Description;
            }
            await _db.SaveChangesAsync();
            return (await GetBuilderStateAsync(id))!;
        }

        public async Task<FormConfigurationBuilderStateDto> DiscardDraftAsync(int id)
        {
            var row = await _db.FormConfigurations.Include(c => c.Steps).FirstOrDefaultAsync(c => c.Id == id)
                      ?? throw new KeyNotFoundException($"FormConfiguration with id {id} not found.");
            // A duplicate's rows are its first version: discarding goes back to them. A configuration made in the
            // builder has no rows to go back to.
            if (row.IsDraft && row.Steps.Count == 0)
                throw new InvalidOperationException(
                    $"'{row.Name}' was never published, so there is no published version to go back to. Delete it on Entity setup instead.");
            if (row.DraftJson != null)
            {
                row.DraftJson = null;
                row.DraftSavedAt = null;
                await _db.SaveChangesAsync();
            }
            return (await GetBuilderStateAsync(id))!;
        }

        public async Task<FormConfigurationDto> PublishAsync(int id)
        {
            var row = await _repo.GetByIdAsync(id)
                      ?? throw new KeyNotFoundException($"FormConfiguration with id {id} not found.");
            if (!row.IsDraft && row.DraftJson == null)
                throw new InvalidOperationException($"'{row.Name}' has no unpublished changes.");

            var working = WorkingCopy(row);
            if (working.Steps == null || working.Steps.Count == 0)
                throw new InvalidOperationException("Add at least one step before publishing.");
            if (working.Steps.Any(s => string.IsNullOrWhiteSpace(s.StepName)))
                throw new InvalidOperationException("Every step needs a name before publishing.");

            var makeDefault = working.IsDefault;
            await using var transaction = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;

            // Every check a builder save ran before #71 (metadata, many-to-many, conditions); a refusal changes nothing.
            try
            {
                await _configurations.UpdateAsync(id, working);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(ex.Message.Replace("Cannot update FormConfiguration:", "Can't publish:"), ex);
            }

            var published = await _db.FormConfigurations.FirstAsync(c => c.Id == id);
            if (makeDefault)
            {
                var others = await _db.FormConfigurations
                    .Where(c => c.EntityTypeName == published.EntityTypeName && c.IsDefault && c.Id != id)
                    .ToListAsync();
                foreach (var other in others)
                {
                    other.IsDefault = false;
                    other.UpdatedAt = DateTime.UtcNow;
                }
            }
            published.IsDefault = makeDefault;
            published.IsDraft = false;
            published.DraftJson = null;
            published.DraftSavedAt = null;
            published.PublishedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            if (transaction != null) await transaction.CommitAsync();

            return (await _configurations.GetByIdAsync(id))!;
        }

        public async Task<FormConfigurationBuilderStateDto> DuplicateAsync(int id, string? name)
        {
            var source = await _db.FormConfigurations.AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new { c.Name, c.IsDraft, StepCount = c.Steps.Count })
                .FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException($"FormConfiguration with id {id} not found.");
            if (source.StepCount == 0)
                throw new InvalidOperationException(
                    $"'{source.Name}' has no published version to copy yet. Publish it first, or keep editing it.");

            var copyName = ConfigurationDraftJson.CopyName(name, source.Name);
            var file = await _portability.ExportAsync(id)
                       ?? throw new KeyNotFoundException($"FormConfiguration with id {id} not found.");
            var json = ConfigurationDraftJson.WithNewGuids(
                FormConfigurationPortabilityService.Serialize(file),
                new[] { "steps", "childFormSteps", "fields" },
                new[] { "stepGuid", "fieldGuid" });
            var copy = System.Text.Json.JsonSerializer.Deserialize<PortableFormConfigurationDto>(json, FormConfigurationPortabilityService.JsonOptions)!;
            copy.Name = copyName;

            var result = await _portability.ImportDraftCopyAsync(copy);
            if (result.Status != FormConfigurationImportStatus.Imported || result.FormConfigurationId == null)
            {
                var why = result.Errors.Count > 0 ? string.Join(" ", result.Errors) : result.Message;
                throw new InvalidOperationException($"'{source.Name}' couldn't be copied: {why}");
            }
            return (await GetBuilderStateAsync(int.Parse(result.FormConfigurationId)))!;
        }

        public Task<bool> IsNeverPublishedAsync(int id) =>
            _db.FormConfigurations.AnyAsync(c => c.Id == id && c.IsDraft);

        public async Task SyncDraftDefaultAsync(int id, bool isDefault)
        {
            var row = await _db.FormConfigurations.FirstOrDefaultAsync(c => c.Id == id);
            if (row?.DraftJson == null) return;
            var draft = ConfigurationDraftJson.Deserialize<FormConfigurationDto>(row.DraftJson);
            if (draft == null || draft.IsDefault == isDefault) return;
            draft.IsDefault = isDefault;
            row.DraftJson = ConfigurationDraftJson.Serialize(draft);
            await _db.SaveChangesAsync();
        }

        // ------------------------------------------------------------------ helpers

        private FormConfigurationDto WorkingCopy(FormConfiguration row)
        {
            FormConfigurationDto? draft = null;
            if (row.DraftJson != null)
            {
                try
                {
                    draft = ConfigurationDraftJson.Deserialize<FormConfigurationDto>(row.DraftJson);
                }
                catch (System.Text.Json.JsonException)
                {
                    draft = null; // an unreadable copy: the builder opens the published version
                }
            }
            var working = draft ?? _mapper.Map<FormConfigurationDto>(row);
            working.Id = row.Id.ToString();
            working.EntityTypeName = row.EntityTypeName;
            working.Steps ??= new List<FormStepDto>();
            return working;
        }

        private async Task<FormConfigurationBuilderStateDto> BuildStateAsync(FormConfiguration row)
        {
            var working = WorkingCopy(row);
            working.IsDraft = row.IsDraft;
            working.HasUnpublishedChanges = row.DraftJson != null;
            working.DraftSavedAt = row.DraftSavedAt?.ToString("O");
            working.PublishedAt = row.PublishedAt?.ToString("O");
            working.CreatedAt = row.CreatedAt.ToString("O");
            working.UpdatedAt = row.UpdatedAt?.ToString("O");

            var openDrafts = await _db.FormSubmissionProgresses
                .CountAsync(p => p.FormConfigurationId == row.Id && OpenDraftStatuses.Contains(p.Status));
            var currentDefault = await _db.FormConfigurations.AsNoTracking()
                .Where(c => c.EntityTypeName == row.EntityTypeName && c.IsDefault && !c.IsDraft && c.Id != row.Id)
                .Select(c => new { c.Id, c.Name })
                .FirstOrDefaultAsync();

            return new FormConfigurationBuilderStateDto
            {
                Configuration = working,
                Published = row.IsDraft ? null : _mapper.Map<FormConfigurationDto>(row),
                IsPublished = !row.IsDraft,
                HasUnpublishedChanges = row.IsDraft || row.DraftJson != null,
                DraftSavedAt = row.DraftSavedAt?.ToString("O"),
                PublishedAt = row.PublishedAt?.ToString("O"),
                OpenDraftCount = openDrafts,
                CurrentDefaultId = currentDefault?.Id.ToString(),
                CurrentDefaultName = currentDefault?.Name
            };
        }

        private static string RequireName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Give the configuration a name.");
            var trimmed = name.Trim();
            if (trimmed.Length > ConfigurationDraftJson.MaxNameLength)
                throw new ArgumentException($"The name may be at most {ConfigurationDraftJson.MaxNameLength} characters.");
            return trimmed;
        }

        private static string SerializeDraft(FormConfigurationDto draft)
        {
            // Publication state is the row's, never part of the copy.
            draft.IsDraft = false;
            draft.HasUnpublishedChanges = false;
            draft.DraftSavedAt = null;
            draft.PublishedAt = null;
            var json = ConfigurationDraftJson.Serialize(draft);
            if (json.Length > ConfigurationDraftJson.MaxDraftLength)
                throw new ArgumentException("The configuration is too large to save.");
            return json;
        }
    }
}
