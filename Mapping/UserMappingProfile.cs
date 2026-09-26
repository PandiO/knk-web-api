using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Mapping
{
    public class UserMappingProfile : Profile
    {
        public UserMappingProfile()
        {
            // ===== User → UserDto =====
            CreateMap<User, UserDto>()
                .ForMember(dest => dest.Id, src => src.MapFrom(src => src.Id))
                .ForMember(dest => dest.Username, src => src.MapFrom(src => src.Username))
                .ForMember(dest => dest.Uuid, src => src.MapFrom(src => src.Uuid))
                .ForMember(dest => dest.Email, src => src.MapFrom(src => src.Email))
                .ForMember(dest => dest.Coins, src => src.MapFrom(src => src.Coins))
                .ForMember(dest => dest.Gems, src => src.MapFrom(src => src.Gems))
                .ForMember(dest => dest.ExperiencePoints, src => src.MapFrom(src => src.ExperiencePoints))
                .ForMember(dest => dest.EmailVerified, src => src.MapFrom(src => src.EmailVerified))
                .ForMember(dest => dest.AccountCreatedVia, src => src.MapFrom(src => src.AccountCreatedVia))
                .ForMember(dest => dest.GatePassThroughMethodDefault, src => src.MapFrom(src => src.GatePassThroughMethodDefault))
                .ForMember(dest => dest.ActiveMode, src => src.MapFrom(src => src.ActiveMode))
                .ForMember(dest => dest.IsFullAccount, src => src.MapFrom(src => src.IsFullAccount))
                .ForMember(dest => dest.IsActive, src => src.MapFrom(src => src.IsActive))
                .ForMember(dest => dest.CreatedAt, src => src.MapFrom(src => src.CreatedAt.ToString("O")))
                // Resolved from ExperiencePoints by UserService.MapToUserDtoAsync after this map
                // runs, not by AutoMapper — User has no title field of its own (IMPLEMENTATION_PLAN.md §4).
                .ForMember(dest => dest.TitleBracketId, opt => opt.Ignore())
                .ForMember(dest => dest.TitleName, opt => opt.Ignore())
                .ForMember(dest => dest.PrestigeExperience, opt => opt.Ignore())
                // Likewise resolved from UserPermissionGroup memberships (IMPLEMENTATION_PLAN.md §5).
                .ForMember(dest => dest.PremiumTierGroupId, opt => opt.Ignore())
                .ForMember(dest => dest.PremiumTierName, opt => opt.Ignore())
                .ForMember(dest => dest.PremiumTierExpiresAt, opt => opt.Ignore())
                // Resolved alongside the premium tier (KNG-7).
                .ForMember(dest => dest.ChatPrimaryColor, opt => opt.Ignore())
                .ForMember(dest => dest.ChatSecondaryColor, opt => opt.Ignore())
                .ForMember(dest => dest.NameColor, opt => opt.Ignore())
                .ForMember(dest => dest.PersonalSalaryMultiplier, src => src.MapFrom(src => src.PersonalSalaryMultiplier))
                .ForMember(dest => dest.PersonalGemBonusMultiplier, src => src.MapFrom(src => (decimal?)src.PersonalGemBonusMultiplier))
                .ForMember(dest => dest.PersonalExpBonusMultiplier, src => src.MapFrom(src => (decimal?)src.PersonalExpBonusMultiplier))
                // MySQL reads DateTime back as Unspecified — mark it UTC so it serializes with a
                // "Z" (same fix UserPermissionGroupService.ToDto already applied for ExpiresAt).
                .ForMember(dest => dest.LastSalaryPayoutAt, src => src.MapFrom(src => DateTime.SpecifyKind(src.LastSalaryPayoutAt, DateTimeKind.Utc)))
                .ForMember(dest => dest.IsOnline, src => src.MapFrom(src => src.IsOnline))
                .ForMember(dest => dest.LastSeenAt, src => src.MapFrom(src => src.LastSeenAt.HasValue ? DateTime.SpecifyKind(src.LastSeenAt.Value, DateTimeKind.Utc) : (DateTime?)null));

            // ===== UserDto → User =====
            // CRITICAL: Ignore PasswordHash to prevent exposure
            CreateMap<UserDto, User>()
                .ForMember(dest => dest.Id, src => src.MapFrom(src => src.Id))
                .ForMember(dest => dest.Username, src => src.MapFrom(src => src.Username))
                .ForMember(dest => dest.Uuid, src => src.MapFrom(src => src.Uuid))
                .ForMember(dest => dest.Email, src => src.MapFrom(src => src.Email))
                .ForMember(dest => dest.Coins, src => src.MapFrom(src => src.Coins))
                .ForMember(dest => dest.Gems, src => src.MapFrom(src => src.Gems))
                .ForMember(dest => dest.ExperiencePoints, src => src.MapFrom(src => src.ExperiencePoints))
                .ForMember(dest => dest.EmailVerified, src => src.MapFrom(src => src.EmailVerified))
                .ForMember(dest => dest.AccountCreatedVia, src => src.MapFrom(src => src.AccountCreatedVia))
                .ForMember(dest => dest.GatePassThroughMethodDefault, src => src.MapFrom(src => src.GatePassThroughMethodDefault))
                // Only writable via PUT /api/users/{id}/active-mode - a generic user update
                // that omits it must not silently reset a vanished player to visible.
                .ForMember(dest => dest.ActiveMode, opt => opt.Ignore())
                .ForMember(dest => dest.IsActive, src => src.MapFrom(src => src.IsActive))
                .ForMember(dest => dest.CreatedAt, src => src.MapFrom(src => DateTime.Parse(src.CreatedAt)))
                .ForMember(dest => dest.PersonalSalaryMultiplier, src => src.MapFrom(src => src.PersonalSalaryMultiplier))
                // Omitted (null) = keep the stored value (KNG-16): without the condition AutoMapper
                // would write 0 for a client that predates these fields.
                .ForMember(dest => dest.PersonalGemBonusMultiplier, opt =>
                {
                    opt.Condition(src => src.PersonalGemBonusMultiplier.HasValue);
                    opt.MapFrom(src => src.PersonalGemBonusMultiplier!.Value);
                })
                .ForMember(dest => dest.PersonalExpBonusMultiplier, opt =>
                {
                    opt.Condition(src => src.PersonalExpBonusMultiplier.HasValue);
                    opt.MapFrom(src => src.PersonalExpBonusMultiplier!.Value);
                })
                // Service-managed only (SalaryService.PayOutAsync) — same convention as ActiveMode:
                // a generic edit that omits it must not reset a user's payout clock.
                .ForMember(dest => dest.LastSalaryPayoutAt, opt => opt.Ignore())
                // Service-managed only (PUT /api/users/{id}/presence) — same convention.
                .ForMember(dest => dest.IsOnline, opt => opt.Ignore())
                .ForMember(dest => dest.LastSeenAt, opt => opt.Ignore())
                .ForMember(dest => dest.PasswordHash, opt => opt.Ignore())
                .ForMember(dest => dest.LastPasswordChangeAt, opt => opt.Ignore())
                .ForMember(dest => dest.LastEmailChangeAt, opt => opt.Ignore())
                .ForMember(dest => dest.DeletedAt, opt => opt.Ignore())
                .ForMember(dest => dest.DeletedReason, opt => opt.Ignore())
                .ForMember(dest => dest.ArchiveUntil, opt => opt.Ignore())
                .ForMember(dest => dest.LinkCodes, opt => opt.Ignore());

            // ===== User → UserSummaryDto =====
            CreateMap<User, UserSummaryDto>()
                .ForMember(dest => dest.Id, src => src.MapFrom(src => src.Id))
                .ForMember(dest => dest.Username, src => src.MapFrom(src => src.Username))
                .ForMember(dest => dest.Uuid, src => src.MapFrom(src => src.Uuid))
                .ForMember(dest => dest.Coins, src => src.MapFrom(src => src.Coins))
                .ForMember(dest => dest.Gems, src => src.MapFrom(src => src.Gems))
                .ForMember(dest => dest.ExperiencePoints, src => src.MapFrom(src => src.ExperiencePoints))
                .ForMember(dest => dest.GatePassThroughMethodDefault, src => src.MapFrom(src => src.GatePassThroughMethodDefault))
                .ForMember(dest => dest.ActiveMode, src => src.MapFrom(src => src.ActiveMode))
                .ForMember(dest => dest.TitleBracketId, opt => opt.Ignore())
                .ForMember(dest => dest.TitleName, opt => opt.Ignore())
                .ForMember(dest => dest.PrestigeExperience, opt => opt.Ignore())
                // Likewise resolved from UserPermissionGroup memberships (IMPLEMENTATION_PLAN.md §5).
                .ForMember(dest => dest.PremiumTierGroupId, opt => opt.Ignore())
                .ForMember(dest => dest.PremiumTierName, opt => opt.Ignore())
                .ForMember(dest => dest.PremiumTierExpiresAt, opt => opt.Ignore())
                // Resolved alongside the premium tier (KNG-7).
                .ForMember(dest => dest.ChatPrimaryColor, opt => opt.Ignore())
                .ForMember(dest => dest.ChatSecondaryColor, opt => opt.Ignore())
                .ForMember(dest => dest.NameColor, opt => opt.Ignore())
                .ForMember(dest => dest.Gender, src => src.MapFrom(src => src.Gender));

            // Note: PersonalSalaryMultiplier/LastSalaryPayoutAt are deliberately not added to
            // UserSummaryDto (the plugin-facing lightweight DTO) — nothing consumes them there
            // yet, per IMPLEMENTATION_PLAN.md §6's knk-web-api-only scope for this phase.

            // ===== User → UserListDto =====
            CreateMap<User, UserListDto>()
                .ForMember(dest => dest.id, src => src.MapFrom(src => src.Id))
                .ForMember(dest => dest.username, src => src.MapFrom(src => src.Username))
                .ForMember(dest => dest.uuid, src => src.MapFrom(src => src.Uuid))
                .ForMember(dest => dest.email, src => src.MapFrom(src => src.Email))
                .ForMember(dest => dest.Coins, src => src.MapFrom(src => src.Coins))
                .ForMember(dest => dest.Gems, src => src.MapFrom(src => src.Gems))
                .ForMember(dest => dest.ExperiencePoints, src => src.MapFrom(src => src.ExperiencePoints))
                .ForMember(dest => dest.IsActive, src => src.MapFrom(src => src.IsActive))
                .ForMember(dest => dest.IsOnline, src => src.MapFrom(src => src.IsOnline))
                .ForMember(dest => dest.LastSeenAt, src => src.MapFrom(src => src.LastSeenAt.HasValue ? DateTime.SpecifyKind(src.LastSeenAt.Value, DateTimeKind.Utc) : (DateTime?)null));

            // ===== UserCreateDto → User =====
            // CRITICAL: Ignore PasswordHash - passwords are hashed separately in the service layer
            CreateMap<UserCreateDto, User>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.Username, src => src.MapFrom(src => src.Username))
                .ForMember(dest => dest.Uuid, src => src.MapFrom(src => src.Uuid))
                .ForMember(dest => dest.Email, src => src.MapFrom(src => src.Email))
                .ForMember(dest => dest.Coins, opt => opt.Ignore())  // Use default from model
                .ForMember(dest => dest.Gems, opt => opt.Ignore())  // Use default from model
                .ForMember(dest => dest.ExperiencePoints, opt => opt.Ignore())  // Use default from model
                .ForMember(dest => dest.CreatedAt, src => src.MapFrom(src => src.CreatedAt))
                .ForMember(dest => dest.PasswordHash, opt => opt.Ignore())  // Hashed in service layer
                .ForMember(dest => dest.EmailVerified, opt => opt.Ignore())
                .ForMember(dest => dest.AccountCreatedVia, opt => opt.Ignore())
                .ForMember(dest => dest.GatePassThroughMethodDefault, opt => opt.Ignore())
                .ForMember(dest => dest.ActiveMode, opt => opt.Ignore())
                .ForMember(dest => dest.PersonalSalaryMultiplier, opt => opt.Ignore())  // Use default from model
                .ForMember(dest => dest.PersonalGemBonusMultiplier, opt => opt.Ignore())  // Use default from model
                .ForMember(dest => dest.PersonalExpBonusMultiplier, opt => opt.Ignore())  // Use default from model
                .ForMember(dest => dest.LastSalaryPayoutAt, opt => opt.Ignore())  // Use default from model
                .ForMember(dest => dest.IsOnline, opt => opt.Ignore())  // Use default from model
                .ForMember(dest => dest.LastSeenAt, opt => opt.Ignore())  // Use default from model
                .ForMember(dest => dest.LastPasswordChangeAt, opt => opt.Ignore())
                .ForMember(dest => dest.LastEmailChangeAt, opt => opt.Ignore())
                .ForMember(dest => dest.IsActive, opt => opt.Ignore())
                .ForMember(dest => dest.DeletedAt, opt => opt.Ignore())
                .ForMember(dest => dest.DeletedReason, opt => opt.Ignore())
                .ForMember(dest => dest.ArchiveUntil, opt => opt.Ignore())
                .ForMember(dest => dest.LinkCodes, opt => opt.Ignore());

            // ===== LinkCode → LinkCodeResponseDto =====
            CreateMap<LinkCode, LinkCodeResponseDto>()
                .ForMember(dest => dest.Code, src => src.MapFrom(src => src.Code))
                .ForMember(dest => dest.ExpiresAt, src => src.MapFrom(src => src.ExpiresAt))
                .ForMember(dest => dest.FormattedCode, src => src.MapFrom(src => FormatLinkCode(src.Code)));
        }

        /// <summary>
        /// Formats link code for display: ABC12XYZ → ABC-12XYZ
        /// </summary>
        private static string FormatLinkCode(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length != 8)
                return code;
            
            return $"{code.Substring(0, 3)}-{code.Substring(3)}";
        }
    }
}
