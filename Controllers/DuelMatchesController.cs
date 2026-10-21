using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Duel;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace knkwebapi_v2.Controllers
{
    /// <summary>
    /// Arena duel matches (arena duels DESIGN.md §4.3, §6.5, §7, §10.3; KNG-213). Served at <c>api/DuelMatches</c>
    /// and the kebab-case <c>api/duel-matches</c>.
    /// <list type="bullet">
    /// <item>Reads (history, a match, by token, the policy): the plugin or any logged-in user.</item>
    /// <item>Lifecycle writes (check, create, start-fight, complete, cancel, abort-unfinished): the game server
    /// only ([RequirePluginService]). Each is idempotent; the plugin may send an Idempotency-Key, and lock-in's
    /// must be <c>duel-lock:{publicToken}</c>.</item>
    /// <item>Staff actions (release, void, revert, confiscate, clear escalation, the policy, restrictions):
    /// knk.admin.duel.review for web staff, or the game server (which checks the node in-game and names the
    /// staff member in X-Acting-User-Id). Restrictions are read under the same rule (they carry staff reasons).</item>
    /// </list>
    /// Refusals answer <c>{ code, message, details }</c>: 404 unknown match/arena/user, 409 a state conflict
    /// (WrongState, TokenReused, PolicyChanged), 400 a malformed request, 422 a rule the duel breaks.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Route("api/duel-matches")]
    public class DuelMatchesController : ControllerBase
    {
        private readonly IDuelMatchService _service;
        private readonly IPermissionResolutionService? _permissions;

        public DuelMatchesController(IDuelMatchService service, IPermissionResolutionService? permissions = null)
        {
            _service = service;
            _permissions = permissions;
        }

        // ===== Reads =====

        /// <summary>History, newest first; every filter optional (userId matches either duelist).</summary>
        [HttpGet]
        public Task<IActionResult> Query([FromQuery] int? userId, [FromQuery] int? arenaId, [FromQuery] DuelMatchStatus? status,
            [FromQuery] DuelReviewState? review, [FromQuery] int limit = 50, CancellationToken ct = default) =>
            Run(async () => Ok(await _service.QueryAsync(userId, arenaId, status, review, limit, ct)));

        [HttpGet("{id:int}")]
        public Task<IActionResult> GetById(int id, CancellationToken ct = default) =>
            Run(async () => await _service.GetByIdAsync(id, ct) is { } match ? Ok(match) : NotFound(NotFoundBody($"Duel match {id} doesn't exist.")));

        /// <summary>The plugin's recovery read (spool scan, lock-in timeout).</summary>
        [HttpGet("by-token/{token}")]
        public Task<IActionResult> GetByToken(string token, CancellationToken ct = default) =>
            Run(async () => await _service.GetByTokenAsync(token, ct) is { } match ? Ok(match) : NotFound(NotFoundBody($"No duel match has token {token}.")));

        [HttpGet("policy")]
        public Task<IActionResult> GetPolicy(CancellationToken ct = default) =>
            Run(async () => Ok(await _service.GetPolicyAsync(ct)));

        // ===== Lifecycle (game server) =====

        /// <summary>The lock-in rules without writing anything: { allowed, refusals[] } (invite and accept pre-checks).</summary>
        [HttpPost("check")]
        [RequirePluginService]
        public Task<IActionResult> Check([FromBody] DuelMatchCreateDto dto, CancellationToken ct = default) =>
            Run(async () => Ok(await _service.CheckAsync(dto, ct)));

        /// <summary>Lock-in: 201 with the match, or 200 with the stored match for a repeated token.</summary>
        [HttpPost]
        [RequirePluginService]
        public Task<IActionResult> Create([FromBody] DuelMatchCreateDto dto, CancellationToken ct = default) =>
            Run(async () =>
            {
                var key = CurrencyHttp.ReadKey(this, required: false, out var keyError);
                if (keyError != null) return keyError;
                if (dto == null) return BadRequest(new { code = nameof(DuelErrorCode.InvalidRequest), message = "A body is required." });
                if (key != null && Guid.TryParse(dto.PublicToken, out var token) && key != DuelMatchService.LockKey(token.ToString("D")))
                {
                    return BadRequest(new { code = nameof(DuelErrorCode.InvalidRequest), message = "The Idempotency-Key must be duel-lock:{publicToken}." });
                }
                var (match, created) = await _service.CreateAsync(dto, ct);
                return created ? Created($"/api/DuelMatches/{match.Id}", match) : Ok(match);
            });

        [HttpPost("{id:int}/start-fight")]
        [RequirePluginService]
        public Task<IActionResult> StartFight(int id, [FromBody] DuelStartFightDto dto, CancellationToken ct = default) =>
            Run(async () => Ok(await _service.StartFightAsync(id, dto, ct)));

        /// <summary>A Win or Forfeit; answers settlement Paid or Held.</summary>
        [HttpPost("{id:int}/complete")]
        [RequirePluginService]
        public Task<IActionResult> Complete(int id, [FromBody] DuelMatchCompleteDto dto, CancellationToken ct = default) =>
            Run(async () => Ok(await _service.CompleteAsync(id, dto, ct)));

        /// <summary>A draw, cancel or abort: the stake is reversed once.</summary>
        [HttpPost("{id:int}/cancel")]
        [RequirePluginService]
        public Task<IActionResult> Cancel(int id, [FromBody] DuelMatchCancelDto dto, CancellationToken ct = default) =>
            Run(async () => Ok(await _service.CancelAsync(id, dto, ct)));

        /// <summary>Plugin startup recovery: every Created/InProgress match is aborted and refunded; held results stay.</summary>
        [HttpPost("abort-unfinished")]
        [RequirePluginService]
        public Task<IActionResult> AbortUnfinished([FromBody] DuelAbortUnfinishedDto? dto, CancellationToken ct = default) =>
            Run(async () => Ok(await _service.AbortUnfinishedAsync((dto ?? new DuelAbortUnfinishedDto()).EndReason, ct)));

        // ===== Staff review =====

        [HttpPost("{id:int}/release")]
        [RequireServiceOrPermission(StaffPermissions.DuelReview)]
        public Task<IActionResult> Release(int id, [FromBody] DuelStaffActionDto? dto, CancellationToken ct = default) =>
            Run(async () => Ok(await _service.ReleaseAsync(id, Actor, dto?.Reason, ct)));

        [HttpPost("{id:int}/void")]
        [RequireServiceOrPermission(StaffPermissions.DuelReview)]
        public Task<IActionResult> Void(int id, [FromBody] DuelStaffActionDto? dto, CancellationToken ct = default) =>
            Run(async () => Ok(await _service.VoidAsync(id, Actor, dto?.Reason ?? "", ct)));

        [HttpPost("{id:int}/revert")]
        [RequireServiceOrPermission(StaffPermissions.DuelReview)]
        public Task<IActionResult> Revert(int id, [FromBody] DuelStaffActionDto? dto, CancellationToken ct = default) =>
            Run(async () => Ok(await _service.RevertAsync(id, Actor, dto?.Reason ?? "", confiscate: false, ct)));

        [HttpPost("{id:int}/confiscate")]
        [RequireServiceOrPermission(StaffPermissions.DuelReview)]
        public Task<IActionResult> Confiscate(int id, [FromBody] DuelStaffActionDto? dto, CancellationToken ct = default) =>
            Run(async () => Ok(await _service.RevertAsync(id, Actor, dto?.Reason ?? "", confiscate: true, ct)));

        [HttpPost("escalation/clear")]
        [RequireServiceOrPermission(StaffPermissions.DuelReview)]
        public Task<IActionResult> ClearEscalation([FromBody] DuelEscalationClearDto dto, CancellationToken ct = default) =>
            Run(async () => Ok(await _service.ClearEscalationAsync(dto, Actor, ct)));

        [HttpPut("policy")]
        [RequireServiceOrPermission(StaffPermissions.DuelReview)]
        public Task<IActionResult> UpdatePolicy([FromBody] DuelPolicyDto dto, CancellationToken ct = default) =>
            Run(async () => Ok(await _service.UpdatePolicyAsync(dto, Actor, ct)));

        // ===== Restrictions (stakes only, DESIGN §10.3) =====

        /// <summary>A player's restrictions (as the player or either side of a pair), or all; active only by default.</summary>
        [HttpGet("restrictions")]
        [RequireServiceOrPermission(StaffPermissions.DuelReview)]
        public Task<IActionResult> GetRestrictions([FromQuery] int? userId, [FromQuery] bool activeOnly = true, CancellationToken ct = default) =>
            Run(async () => Ok(await _service.GetRestrictionsAsync(userId, activeOnly, ct)));

        [HttpPost("restrictions")]
        [RequireServiceOrPermission(StaffPermissions.DuelReview)]
        public Task<IActionResult> CreateRestriction([FromBody] DuelRestrictionCreateDto dto, CancellationToken ct = default) =>
            Run(async () =>
            {
                var created = await _service.CreateRestrictionAsync(dto, Actor, ct);
                return Created($"/api/DuelMatches/restrictions/{created.Id}", created);
            });

        [HttpPost("restrictions/{id:int}/lift")]
        [RequireServiceOrPermission(StaffPermissions.DuelReview)]
        public Task<IActionResult> LiftRestriction(int id, [FromBody] DuelStaffActionDto? dto, CancellationToken ct = default) =>
            Run(async () => Ok(await _service.LiftRestrictionAsync(id, Actor, dto?.Reason, ct)));

        // ===== Helpers =====

        /// <summary>The web staff member, or the in-game one the plugin names; null for the server itself.</summary>
        private int? Actor => HttpContext?.GetKnkCaller().ActorUserId;

        private static object NotFoundBody(string message) => new { code = "NotFound", message };

        /// <summary>
        /// Review data is staff-only (developer, 2026-10-21): reviewTier, reviewSignals and reviewDetails go only to
        /// the plugin and to holders of knk.admin.duel.review; everyone else sees the review state and the result.
        /// </summary>
        private async Task<bool> SeesReviewDataAsync()
        {
            var caller = HttpContext?.GetKnkCaller();
            if (caller == null) return false;
            if (caller.IsPluginService) return true;
            if (caller.WebUserId is not int userId || _permissions == null) return false;
            return (await _permissions.CheckAsync(userId, StaffPermissions.DuelReview))?.Allowed == true;
        }

        private static void HideReviewData(DuelMatchDto? match)
        {
            if (match == null) return;
            match.ReviewTier = null;
            match.ReviewSignals = new List<string>();
            match.ReviewDetails = null;
        }

        private async Task<IActionResult> FilterReviewDataAsync(IActionResult result)
        {
            if (result is not ObjectResult { Value: { } value } || await SeesReviewDataAsync()) return result;
            switch (value)
            {
                case DuelMatchDto match: HideReviewData(match); break;
                case IEnumerable<DuelMatchDto> matches: foreach (var m in matches) HideReviewData(m); break;
                case DuelResultDto r: HideReviewData(r.Match); break;
            }
            return result;
        }

        private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
        {
            try
            {
                return await FilterReviewDataAsync(await action());
            }
            catch (DuelException ex)
            {
                var body = new { code = ex.Code.ToString(), message = ex.Message, details = ex.Details };
                return ex.Code switch
                {
                    DuelErrorCode.ArenaNotFound or DuelErrorCode.MatchNotFound or DuelErrorCode.UserNotFound
                        or DuelErrorCode.RestrictionNotFound => NotFound(body),
                    DuelErrorCode.WrongState or DuelErrorCode.TokenReused or DuelErrorCode.PolicyChanged => Conflict(body),
                    DuelErrorCode.InvalidRequest => BadRequest(body),
                    _ => UnprocessableEntity(body)
                };
            }
            catch (CurrencyException ex)
            {
                return CurrencyHttp.ToResult(this, ex);
            }
        }
    }
}
