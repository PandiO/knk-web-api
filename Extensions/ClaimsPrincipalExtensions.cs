using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace knkwebapi_v2.Extensions
{
    public static class ClaimsPrincipalExtensions
    {
        /// <summary>
        /// The authenticated caller's user id, or null if unauthenticated/unresolvable — used as
        /// the actor id for audit-log writes (docs/specs/user-management/DESIGN.md §4: "nullable
        /// for system-initiated changes"; here it's nullable because these endpoints don't
        /// currently require [Authorize], not because the actor is a system process — see
        /// AuditLogEntry.ActorUserId's own doc comment). Same claim lookup as
        /// UsersController.GetUserIdFromClaims, shared here so the new group/grant controllers
        /// don't duplicate it a third time.
        /// </summary>
        public static int? GetUserId(this ClaimsPrincipal principal)
        {
            var userIdClaim = principal.FindFirst("uid")
                ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)
                ?? principal.FindFirst(ClaimTypes.NameIdentifier);

            if (userIdClaim == null)
            {
                return null;
            }

            return int.TryParse(userIdClaim.Value, out var userId) ? userId : (int?)null;
        }
    }
}
