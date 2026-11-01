using System;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    public class WorkflowMappingProfile : Profile
    {
        public WorkflowMappingProfile()
        {
            // WorkflowSession
            CreateMap<WorkflowSessionCreateDto, WorkflowSession>()
                .ForMember(d => d.Id, o => o.Ignore())
                .ForMember(d => d.SessionGuid, o => o.MapFrom(_ => Guid.NewGuid()))
                .ForMember(d => d.Status, o => o.MapFrom(_ => "InProgress"))
                .ForMember(d => d.CreatedAt, o => o.MapFrom(_ => DateTime.UtcNow))
                .ForMember(d => d.UpdatedAt, o => o.Ignore())
                .ForMember(d => d.CompletedAt, o => o.Ignore())
                .ForMember(d => d.RowVersion, o => o.Ignore())
                .ForMember(d => d.User, o => o.Ignore())
                .ForMember(d => d.FormConfiguration, o => o.Ignore())
                .ForMember(d => d.Steps, o => o.Ignore())
                .ForMember(d => d.WorldTasks, o => o.Ignore());

            CreateMap<WorkflowSession, WorkflowSessionReadDto>()
                .ForMember(d => d.CreatedAt, o => o.MapFrom(s => s.CreatedAt.ToString("O")))
                .ForMember(d => d.UpdatedAt, o => o.MapFrom(s => s.UpdatedAt.HasValue ? s.UpdatedAt.Value.ToString("O") : null))
                .ForMember(d => d.CompletedAt, o => o.MapFrom(s => s.CompletedAt.HasValue ? s.CompletedAt.Value.ToString("O") : null));

            // StepProgress
            CreateMap<StepProgressCreateDto, StepProgress>()
                .ForMember(d => d.Id, o => o.Ignore())
                .ForMember(d => d.Status, o => o.MapFrom(_ => "Pending"))
                .ForMember(d => d.CreatedAt, o => o.MapFrom(_ => DateTime.UtcNow))
                .ForMember(d => d.UpdatedAt, o => o.Ignore())
                .ForMember(d => d.CompletedAt, o => o.Ignore())
                .ForMember(d => d.WorkflowSession, o => o.Ignore());

            CreateMap<StepProgress, StepProgressReadDto>()
                .ForMember(d => d.CreatedAt, o => o.MapFrom(s => s.CreatedAt.ToString("O")))
                .ForMember(d => d.UpdatedAt, o => o.MapFrom(s => s.UpdatedAt.HasValue ? s.UpdatedAt.Value.ToString("O") : null))
                .ForMember(d => d.CompletedAt, o => o.MapFrom(s => s.CompletedAt.HasValue ? s.CompletedAt.Value.ToString("O") : null));

            // WorldTask
            CreateMap<WorldTaskCreateDto, WorldTask>()
                .ForMember(d => d.Id, o => o.Ignore())
                .ForMember(d => d.LinkCode, o => o.Ignore()) // Generated in service
                .ForMember(d => d.Status, o => o.MapFrom(_ => "Pending"))
                .ForMember(d => d.CreatedAt, o => o.MapFrom(_ => DateTime.UtcNow))
                .ForMember(d => d.InputJson, o => o.MapFrom(s => s.InputJson ?? s.PayloadJson))
                .ForMember(d => d.ClaimedAt, o => o.Ignore())
                .ForMember(d => d.UpdatedAt, o => o.Ignore())
                .ForMember(d => d.CompletedAt, o => o.Ignore())
                .ForMember(d => d.ClaimedByServerId, o => o.Ignore())
                .ForMember(d => d.ClaimedByMinecraftUsername, o => o.Ignore())
                .ForMember(d => d.OutputJson, o => o.Ignore())
                .ForMember(d => d.ErrorMessage, o => o.Ignore())
                .ForMember(d => d.PayloadJson, o => o.MapFrom(s => s.PayloadJson ?? s.InputJson))
                .ForMember(d => d.WorkflowSession, o => o.Ignore())
                .ForMember(d => d.AssignedUser, o => o.Ignore())
                // #180: set by WorldTaskService (creator from the JWT, expiry from the options), never from the body
                .ForMember(d => d.FormSubmissionProgress, o => o.Ignore())
                .ForMember(d => d.AppliedAt, o => o.Ignore())
                .ForMember(d => d.CreatedByUserId, o => o.Ignore())
                .ForMember(d => d.CreatedByUser, o => o.Ignore())
                .ForMember(d => d.TargetUserId, o => o.Ignore())
                .ForMember(d => d.TargetUser, o => o.Ignore())
                .ForMember(d => d.OfferState, o => o.Ignore())
                .ForMember(d => d.OfferedAt, o => o.Ignore())
                .ForMember(d => d.ClaimedByUuid, o => o.Ignore())
                .ForMember(d => d.ClaimedByUserId, o => o.Ignore())
                .ForMember(d => d.ClaimedByUser, o => o.Ignore())
                .ForMember(d => d.ExpiresAt, o => o.Ignore())
                .ForMember(d => d.LeaseUntil, o => o.Ignore())
                .ForMember(d => d.ReleaseCount, o => o.Ignore())
                .ForMember(d => d.RowVersion, o => o.Ignore());

            CreateMap<WorldTask, WorldTaskReadDto>()
                .ForMember(d => d.CreatedAt, o => o.MapFrom(s => s.CreatedAt.ToString("O")))
                .ForMember(d => d.ClaimedAt, o => o.MapFrom(s => s.ClaimedAt.HasValue ? s.ClaimedAt.Value.ToString("O") : null))
                .ForMember(d => d.UpdatedAt, o => o.MapFrom(s => s.UpdatedAt.HasValue ? s.UpdatedAt.Value.ToString("O") : null))
                .ForMember(d => d.CompletedAt, o => o.MapFrom(s => s.CompletedAt.HasValue ? s.CompletedAt.Value.ToString("O") : null))
                // #180
                .ForMember(d => d.EntityType, o => o.MapFrom(s => s.WorkflowSession != null ? s.WorkflowSession.EntityTypeName : null))
                .ForMember(d => d.ExpiresAt, o => o.MapFrom(s => s.ExpiresAt.HasValue ? DateTime.SpecifyKind(s.ExpiresAt.Value, DateTimeKind.Utc).ToString("O") : null))
                .ForMember(d => d.LeaseUntil, o => o.MapFrom(s => s.LeaseUntil.HasValue ? DateTime.SpecifyKind(s.LeaseUntil.Value, DateTimeKind.Utc).ToString("O") : null))
                .ForMember(d => d.AppliedAt, o => o.MapFrom(s => s.AppliedAt.HasValue ? DateTime.SpecifyKind(s.AppliedAt.Value, DateTimeKind.Utc).ToString("O") : null));
        }
    }
}
