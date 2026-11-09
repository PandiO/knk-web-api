using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// PandiO/knk-workspace#71: the display builder's draft and publish steps, as <see cref="FormConfigurationDraftService"/>
    /// for forms. Display configurations already had <c>IsDraft</c> (never published); what is new is that the builder's
    /// changes to a <b>published</b> configuration wait in <c>DraftJson</c> until Publish, so detail pages keep showing
    /// the published version while staff work on the next one.
    /// </summary>
    public interface IDisplayConfigurationDraftService
    {
        Task<DisplayConfigurationBuilderStateDto?> GetBuilderStateAsync(int id);
        Task<DisplayConfigurationBuilderStateDto> CreateDraftAsync(DisplayConfigurationDto draft);
        Task<DisplayConfigurationBuilderStateDto> SaveDraftAsync(int id, DisplayConfigurationDto draft);
        Task<DisplayConfigurationBuilderStateDto> DiscardDraftAsync(int id);
        /// <summary>Publishes the saved copy, or a never-published configuration's rows (the older Publish of Entity setup).</summary>
        Task<DisplayConfigurationDto> PublishAsync(int id);
        Task<DisplayConfigurationBuilderStateDto> DuplicateAsync(int id, string? name);
        Task SyncDraftDefaultAsync(int id, bool isDefault);
    }

    public class DisplayConfigurationDraftService : IDisplayConfigurationDraftService
    {
        private readonly KnKDbContext _db;
        private readonly IDisplayConfigurationRepository _repo;
        private readonly IDisplayConfigurationService _configurations;
        private readonly IDisplayConfigurationPortabilityService _portability;
        private readonly IMapper _mapper;

        public DisplayConfigurationDraftService(
            KnKDbContext db,
            IDisplayConfigurationRepository repo,
            IDisplayConfigurationService configurations,
            IDisplayConfigurationPortabilityService portability,
            IMapper mapper)
        {
            _db = db;
            _repo = repo;
            _configurations = configurations;
            _portability = portability;
            _mapper = mapper;
        }

        public async Task<DisplayConfigurationBuilderStateDto?> GetBuilderStateAsync(int id)
        {
            if (id <= 0) return null;
            var row = await _repo.GetByIdAsync(id);
            return row == null ? null : await BuildStateAsync(row);
        }

        public async Task<DisplayConfigurationBuilderStateDto> CreateDraftAsync(DisplayConfigurationDto draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (string.IsNullOrWhiteSpace(draft.EntityTypeName)) throw new ArgumentException("Choose an entity first.");
            var name = RequireName(draft.Name);

            draft.Id = null;
            var row = new DisplayConfiguration
            {
                Name = name,
                EntityTypeName = draft.EntityTypeName.Trim(),
                Description = string.IsNullOrWhiteSpace(draft.Description) ? null : draft.Description,
                IsDefault = false,
                IsDraft = true,
                SectionOrderJson = "[]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            draft.EntityTypeName = row.EntityTypeName;
            draft.Name = name;
            draft.ConfigurationGuid = row.ConfigurationGuid.ToString();
            row.DraftJson = SerializeDraft(draft);
            row.DraftSavedAt = DateTime.UtcNow;

            _db.DisplayConfigurations.Add(row);
            await _db.SaveChangesAsync();
            return (await GetBuilderStateAsync(row.Id))!;
        }

        public async Task<DisplayConfigurationBuilderStateDto> SaveDraftAsync(int id, DisplayConfigurationDto draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            var row = await _db.DisplayConfigurations.FirstOrDefaultAsync(c => c.Id == id)
                      ?? throw new KeyNotFoundException($"DisplayConfiguration with id {id} not found.");
            if (!string.Equals(row.EntityTypeName, draft.EntityTypeName?.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"This configuration is for {row.EntityTypeName}; its entity can't change.");
            var name = RequireName(draft.Name);

            draft.Id = id.ToString();
            draft.EntityTypeName = row.EntityTypeName;
            draft.Name = name;
            draft.ConfigurationGuid = row.ConfigurationGuid.ToString();
            row.DraftJson = SerializeDraft(draft);
            row.DraftSavedAt = DateTime.UtcNow;
            if (row.IsDraft)
            {
                row.Name = name;
                row.Description = string.IsNullOrWhiteSpace(draft.Description) ? null : draft.Description;
            }
            await _db.SaveChangesAsync();
            return (await GetBuilderStateAsync(id))!;
        }

        public async Task<DisplayConfigurationBuilderStateDto> DiscardDraftAsync(int id)
        {
            var row = await _db.DisplayConfigurations.Include(c => c.Sections).FirstOrDefaultAsync(c => c.Id == id)
                      ?? throw new KeyNotFoundException($"DisplayConfiguration with id {id} not found.");
            // A duplicate's (or an older draft's) rows are its first version: discarding goes back to them.
            if (row.IsDraft && row.Sections.Count == 0)
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

        public async Task<DisplayConfigurationDto> PublishAsync(int id)
        {
            var row = await _repo.GetByIdAsync(id)
                      ?? throw new KeyNotFoundException($"DisplayConfiguration with id {id} not found.");
            if (!row.IsDraft && row.DraftJson == null)
                throw new InvalidOperationException($"'{row.Name}' is published and has no unpublished changes.");

            var draft = ReadDraft(row);
            var makeDefault = draft?.IsDefault ?? row.IsDefault;
            if (draft != null && (draft.Sections == null || draft.Sections.Count == 0))
                throw new InvalidOperationException("Add at least one section before publishing.");
            if (draft == null && row.SectionOrderJson is null or "[]")
                throw new InvalidOperationException($"Cannot publish DisplayConfiguration {id}: No sections configured.");

            await using var transaction = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;

            if (draft != null)
            {
                // The default moves below, after the copy is applied (UpdateAsync refuses a second default).
                draft.IsDefault = false;
                draft.SectionOrderJson ??= ConfigurationDraftJson.Serialize(draft.Sections.Select(s => s.SectionGuid ?? s.Id).ToList());
                await _configurations.UpdateAsync(id, draft);
            }

            var published = await _db.DisplayConfigurations.FirstAsync(c => c.Id == id);
            if (makeDefault)
            {
                var others = await _db.DisplayConfigurations
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
            published.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            if (transaction != null) await transaction.CommitAsync();

            return (await _configurations.GetByIdAsync(id))!;
        }

        public async Task<DisplayConfigurationBuilderStateDto> DuplicateAsync(int id, string? name)
        {
            var source = await _db.DisplayConfigurations.AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new { c.Name, SectionCount = c.Sections.Count })
                .FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException($"DisplayConfiguration with id {id} not found.");
            if (source.SectionCount == 0)
                throw new InvalidOperationException(
                    $"'{source.Name}' has no published version to copy yet. Publish it first, or keep editing it.");

            var copyName = ConfigurationDraftJson.CopyName(name, source.Name);
            var file = await _portability.ExportAsync(id)
                       ?? throw new KeyNotFoundException($"DisplayConfiguration with id {id} not found.");
            var json = ConfigurationDraftJson.WithNewGuids(
                DisplayConfigurationPortabilityService.Serialize(file),
                new[] { "sections", "subSections", "fields" },
                new[] { "sectionGuid", "fieldGuid" });
            var copy = System.Text.Json.JsonSerializer.Deserialize<PortableDisplayConfigurationDto>(json, FormConfigurationPortabilityService.JsonOptions)!;
            copy.Name = copyName;

            var result = await _portability.ImportDraftCopyAsync(copy);
            if (result.Status != FormConfigurationImportStatus.Imported || result.DisplayConfigurationId == null)
            {
                var why = result.Errors.Count > 0 ? string.Join(" ", result.Errors) : result.Message;
                throw new InvalidOperationException($"'{source.Name}' couldn't be copied: {why}");
            }
            return (await GetBuilderStateAsync(int.Parse(result.DisplayConfigurationId)))!;
        }

        public async Task SyncDraftDefaultAsync(int id, bool isDefault)
        {
            var row = await _db.DisplayConfigurations.FirstOrDefaultAsync(c => c.Id == id);
            if (row?.DraftJson == null) return;
            var draft = ReadDraft(row);
            if (draft == null || draft.IsDefault == isDefault) return;
            draft.IsDefault = isDefault;
            row.DraftJson = ConfigurationDraftJson.Serialize(draft);
            await _db.SaveChangesAsync();
        }

        // ------------------------------------------------------------------ helpers

        private static DisplayConfigurationDto? ReadDraft(DisplayConfiguration row)
        {
            if (row.DraftJson == null) return null;
            try
            {
                return ConfigurationDraftJson.Deserialize<DisplayConfigurationDto>(row.DraftJson);
            }
            catch (System.Text.Json.JsonException)
            {
                return null; // an unreadable copy: the builder opens the published version
            }
        }

        private async Task<DisplayConfigurationBuilderStateDto> BuildStateAsync(DisplayConfiguration row)
        {
            var rows = _mapper.Map<DisplayConfigurationDto>(row);
            var working = ReadDraft(row) ?? _mapper.Map<DisplayConfigurationDto>(row);
            working.Id = row.Id.ToString();
            working.EntityTypeName = row.EntityTypeName;
            working.ConfigurationGuid = row.ConfigurationGuid.ToString();
            working.IsDraft = row.IsDraft;
            working.HasUnpublishedChanges = row.DraftJson != null;
            working.DraftSavedAt = row.DraftSavedAt?.ToString("o");
            working.PublishedAt = row.PublishedAt?.ToString("o");
            working.CreatedAt = row.CreatedAt.ToString("o");
            working.UpdatedAt = row.UpdatedAt?.ToString("o");
            working.Sections ??= new List<DisplaySectionDto>();

            var currentDefault = await _db.DisplayConfigurations.AsNoTracking()
                .Where(c => c.EntityTypeName == row.EntityTypeName && c.IsDefault && !c.IsDraft && c.Id != row.Id)
                .Select(c => new { c.Id, c.Name })
                .FirstOrDefaultAsync();

            return new DisplayConfigurationBuilderStateDto
            {
                Configuration = working,
                Published = row.IsDraft ? null : rows,
                IsPublished = !row.IsDraft,
                HasUnpublishedChanges = row.IsDraft || row.DraftJson != null,
                DraftSavedAt = row.DraftSavedAt?.ToString("o"),
                PublishedAt = row.PublishedAt?.ToString("o"),
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

        private static string SerializeDraft(DisplayConfigurationDto draft)
        {
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
