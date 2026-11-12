using System;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    public class DisplayMappingProfile : Profile
    {
        // Helper methods voor type conversie
        // Handles temporary IDs from frontend (e.g., "temp-123456-0.789") by returning 0
        private static int ToInt(string? s)
        {
            if (string.IsNullOrEmpty(s) || s.StartsWith("temp-", StringComparison.OrdinalIgnoreCase))
                return 0;
            return int.TryParse(s, out var result) ? result : 0;
        }

        private static int? ToNullableInt(string? s)
        {
            if (string.IsNullOrEmpty(s) || s.StartsWith("temp-", StringComparison.OrdinalIgnoreCase))
                return null;
            return int.TryParse(s, out var result) ? result : null;
        }

        public DisplayMappingProfile()
        {
            // DisplayConfiguration mappings
            CreateMap<DisplayConfiguration, DisplayConfigurationDto>()
                .ForMember(d => d.Id, o => o.MapFrom(s => s.Id.ToString()))
                .ForMember(d => d.ConfigurationGuid, o => o.MapFrom(s => s.ConfigurationGuid.ToString()))
                .ForMember(d => d.Name, o => o.MapFrom(s => s.Name))
                .ForMember(d => d.EntityTypeName, o => o.MapFrom(s => s.EntityTypeName))
                .ForMember(d => d.IsDefault, o => o.MapFrom(s => s.IsDefault))
                .ForMember(d => d.Description, o => o.MapFrom(s => s.Description))
                .ForMember(d => d.SectionOrderJson, o => o.MapFrom(s => s.SectionOrderJson))
                .ForMember(d => d.IsDraft, o => o.MapFrom(s => s.IsDraft))
                .ForMember(d => d.CreatedAt, o => o.MapFrom(s => s.CreatedAt.ToString("o")))
                .ForMember(d => d.UpdatedAt, o => o.MapFrom(s => s.UpdatedAt.HasValue ? s.UpdatedAt.Value.ToString("o") : null))
                .ForMember(d => d.HasUnpublishedChanges, o => o.MapFrom(s => s.DraftJson != null))
                .ForMember(d => d.DraftSavedAt, o => o.MapFrom(s => s.DraftSavedAt.HasValue ? s.DraftSavedAt.Value.ToString("o") : null))
                .ForMember(d => d.PublishedAt, o => o.MapFrom(s => s.PublishedAt.HasValue ? s.PublishedAt.Value.ToString("o") : null))
                .ForMember(d => d.Sections, o => o.MapFrom(s => s.Sections));

            CreateMap<DisplayConfigurationDto, DisplayConfiguration>()
                .ForMember(d => d.Id, o => o.MapFrom(s => ToInt(s.Id)))
                .ForMember(d => d.ConfigurationGuid, o => o.MapFrom(s => 
                    string.IsNullOrEmpty(s.ConfigurationGuid) ? Guid.NewGuid() : Guid.Parse(s.ConfigurationGuid)))
                .ForMember(d => d.Name, o => o.MapFrom(s => s.Name))
                .ForMember(d => d.EntityTypeName, o => o.MapFrom(s => s.EntityTypeName))
                .ForMember(d => d.IsDefault, o => o.MapFrom(s => s.IsDefault))
                .ForMember(d => d.Description, o => o.MapFrom(s => s.Description))
                .ForMember(d => d.SectionOrderJson, o => o.MapFrom(s => s.SectionOrderJson ?? "[]"))
                .ForMember(d => d.IsDraft, o => o.MapFrom(s => s.IsDraft))
                .ForMember(d => d.CreatedAt, o => o.MapFrom(s => 
                    string.IsNullOrEmpty(s.CreatedAt) ? DateTime.UtcNow : DateTime.Parse(s.CreatedAt)))
                .ForMember(d => d.UpdatedAt, o => o.MapFrom(s => 
                    string.IsNullOrEmpty(s.UpdatedAt) ? (DateTime?)null : DateTime.Parse(s.UpdatedAt)))
                .ForMember(d => d.Sections, o => o.MapFrom(s => s.Sections))
                // #71: the builder's working copy is DisplayConfigurationDraftService's, never taken from a body.
                .ForMember(d => d.DraftJson, o => o.Ignore())
                .ForMember(d => d.DraftSavedAt, o => o.Ignore())
                .ForMember(d => d.PublishedAt, o => o.Ignore());

            // DisplaySection mappings
            CreateMap<DisplaySection, DisplaySectionDto>()
                .ForMember(d => d.Id, o => o.MapFrom(s => s.Id.ToString()))
                .ForMember(d => d.SectionGuid, o => o.MapFrom(s => s.SectionGuid.ToString()))
                .ForMember(d => d.DisplayConfigurationId, o => o.MapFrom(s => 
                    s.DisplayConfigurationId.HasValue ? s.DisplayConfigurationId.Value.ToString() : null))
                .ForMember(d => d.SectionName, o => o.MapFrom(s => s.SectionName))
                .ForMember(d => d.Description, o => o.MapFrom(s => s.Description))
                .ForMember(d => d.IsReusable, o => o.MapFrom(s => s.IsReusable))
                .ForMember(d => d.SourceSectionId, o => o.MapFrom(s => 
                    s.SourceSectionId.HasValue ? s.SourceSectionId.Value.ToString() : null))
                .ForMember(d => d.IsLinkedToSource, o => o.MapFrom(s => s.IsLinkedToSource))
                .ForMember(d => d.FieldOrderJson, o => o.MapFrom(s => s.FieldOrderJson))
                .ForMember(d => d.RelatedEntityPropertyName, o => o.MapFrom(s => s.RelatedEntityPropertyName))
                .ForMember(d => d.RelatedEntityTypeName, o => o.MapFrom(s => s.RelatedEntityTypeName))
                .ForMember(d => d.IsCollection, o => o.MapFrom(s => s.IsCollection))
                .ForMember(d => d.ActionButtonsConfigJson, o => o.MapFrom(s => s.ActionButtonsConfigJson))
                .ForMember(d => d.ParentSectionId, o => o.MapFrom(s => 
                    s.ParentSectionId.HasValue ? s.ParentSectionId.Value.ToString() : null))
                .ForMember(d => d.CreatedAt, o => o.MapFrom(s => s.CreatedAt.ToString("o")))
                .ForMember(d => d.UpdatedAt, o => o.MapFrom(s => 
                    s.UpdatedAt.HasValue ? s.UpdatedAt.Value.ToString("o") : null))
                .ForMember(d => d.Fields, o => o.MapFrom(s => s.Fields))
                .ForMember(d => d.SubSections, o => o.MapFrom(s => s.SubSections));

            CreateMap<DisplaySectionDto, DisplaySection>()
                .ForMember(d => d.Id, o => o.MapFrom(s => ToInt(s.Id)))
                .ForMember(d => d.SectionGuid, o => o.MapFrom(s => 
                    string.IsNullOrEmpty(s.SectionGuid) ? Guid.NewGuid() : Guid.Parse(s.SectionGuid)))
                .ForMember(d => d.DisplayConfigurationId, o => o.MapFrom(s => ToNullableInt(s.DisplayConfigurationId)))
                .ForMember(d => d.SectionName, o => o.MapFrom(s => s.SectionName))
                .ForMember(d => d.Description, o => o.MapFrom(s => s.Description))
                .ForMember(d => d.IsReusable, o => o.MapFrom(s => s.IsReusable))
                .ForMember(d => d.SourceSectionId, o => o.MapFrom(s => ToNullableInt(s.SourceSectionId)))
                .ForMember(d => d.IsLinkedToSource, o => o.MapFrom(s => s.IsLinkedToSource))
                .ForMember(d => d.FieldOrderJson, o => o.MapFrom(s => s.FieldOrderJson ?? "[]"))
                .ForMember(d => d.RelatedEntityPropertyName, o => o.MapFrom(s => s.RelatedEntityPropertyName))
                .ForMember(d => d.RelatedEntityTypeName, o => o.MapFrom(s => s.RelatedEntityTypeName))
                .ForMember(d => d.IsCollection, o => o.MapFrom(s => s.IsCollection))
                .ForMember(d => d.ActionButtonsConfigJson, o => o.MapFrom(s => s.ActionButtonsConfigJson ?? "{}"))
                .ForMember(d => d.ParentSectionId, o => o.MapFrom(s => ToNullableInt(s.ParentSectionId)))
                .ForMember(d => d.CreatedAt, o => o.MapFrom(s => 
                    string.IsNullOrEmpty(s.CreatedAt) ? DateTime.UtcNow : DateTime.Parse(s.CreatedAt)))
                .ForMember(d => d.UpdatedAt, o => o.MapFrom(s => 
                    string.IsNullOrEmpty(s.UpdatedAt) ? (DateTime?)null : DateTime.Parse(s.UpdatedAt)))
                .ForMember(d => d.DisplayConfiguration, o => o.Ignore())
                .ForMember(d => d.ParentSection, o => o.Ignore())
                .ForMember(d => d.Fields, o => o.MapFrom(s => s.Fields))
                .ForMember(d => d.SubSections, o => o.MapFrom(s => s.SubSections));

            // DisplayField mappings
            CreateMap<DisplayField, DisplayFieldDto>()
                .ForMember(d => d.Id, o => o.MapFrom(s => s.Id.ToString()))
                .ForMember(d => d.FieldGuid, o => o.MapFrom(s => s.FieldGuid.ToString()))
                .ForMember(d => d.DisplaySectionId, o => o.MapFrom(s => 
                    s.DisplaySectionId.HasValue ? s.DisplaySectionId.Value.ToString() : null))
                .ForMember(d => d.FieldName, o => o.MapFrom(s => s.FieldName))
                .ForMember(d => d.Label, o => o.MapFrom(s => s.Label))
                .ForMember(d => d.Description, o => o.MapFrom(s => s.Description))
                .ForMember(d => d.TemplateText, o => o.MapFrom(s => s.TemplateText))
                .ForMember(d => d.FieldType, o => o.MapFrom(s => s.FieldType))
                .ForMember(d => d.IsReusable, o => o.MapFrom(s => s.IsReusable))
                .ForMember(d => d.SourceFieldId, o => o.MapFrom(s => 
                    s.SourceFieldId.HasValue ? s.SourceFieldId.Value.ToString() : null))
                .ForMember(d => d.IsLinkedToSource, o => o.MapFrom(s => s.IsLinkedToSource))
                .ForMember(d => d.CreatedAt, o => o.MapFrom(s => s.CreatedAt.ToString("o")))
                .ForMember(d => d.UpdatedAt, o => o.MapFrom(s => 
                    s.UpdatedAt.HasValue ? s.UpdatedAt.Value.ToString("o") : null))
                .ForMember(d => d.IsEditableInDisplay, o => o.MapFrom(s => s.IsEditableInDisplay));

            CreateMap<DisplayFieldDto, DisplayField>()
                .ForMember(d => d.Id, o => o.MapFrom(s => ToInt(s.Id)))
                .ForMember(d => d.FieldGuid, o => o.MapFrom(s => 
                    string.IsNullOrEmpty(s.FieldGuid) ? Guid.NewGuid() : Guid.Parse(s.FieldGuid)))
                .ForMember(d => d.DisplaySectionId, o => o.MapFrom(s => ToNullableInt(s.DisplaySectionId)))
                .ForMember(d => d.FieldName, o => o.MapFrom(s => s.FieldName))
                .ForMember(d => d.Label, o => o.MapFrom(s => s.Label))
                .ForMember(d => d.Description, o => o.MapFrom(s => s.Description))
                .ForMember(d => d.TemplateText, o => o.MapFrom(s => s.TemplateText))
                .ForMember(d => d.FieldType, o => o.MapFrom(s => s.FieldType))
                .ForMember(d => d.IsReusable, o => o.MapFrom(s => s.IsReusable))
                .ForMember(d => d.SourceFieldId, o => o.MapFrom(s => ToNullableInt(s.SourceFieldId)))
                .ForMember(d => d.IsLinkedToSource, o => o.MapFrom(s => s.IsLinkedToSource))
                    .ForMember(d => d.IsEditableInDisplay, o => o.MapFrom(s => s.IsEditableInDisplay))
                .ForMember(d => d.CreatedAt, o => o.MapFrom(s => 
                    string.IsNullOrEmpty(s.CreatedAt) ? DateTime.UtcNow : DateTime.Parse(s.CreatedAt)))
                .ForMember(d => d.UpdatedAt, o => o.MapFrom(s => 
                    string.IsNullOrEmpty(s.UpdatedAt) ? (DateTime?)null : DateTime.Parse(s.UpdatedAt)))
                .ForMember(d => d.DisplaySection, o => o.Ignore());
        }
    }
}
