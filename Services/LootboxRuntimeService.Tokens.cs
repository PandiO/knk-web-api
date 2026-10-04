using System.Diagnostics;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Lootbox;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Lootbox token items (docs/specs/lootboxes/IMPLEMENTATION_PLAN.md Phase 5): v1's "Sword Box" consumables, made
    /// unforgeable. The API issues each token with a random id the plugin stamps into the item's PDC; the item is
    /// identified by that id only (never its name, v1's anvil-rename exploit). Opening it redeems the token: the shared
    /// roll, UTC daily cap, ItemInstance mint and drop log, in one transaction that also flips the token to Redeemed.
    /// A picked-up world box (reason WorldPickup, <c>LootboxRuntimeService.Pickup.cs</c>) is opened the same way.
    /// <para>
    /// A redeem locks the opener's user row, replays a stored claim for the same idempotency key
    /// (a retry of the same click), then refuses anything but an Issued token. The flip to Redeemed is a conditional
    /// update on the <c>[ConcurrencyCheck]</c> Status, so two copies of a duplicated item opened at once (even by two
    /// players, on two servers) produce exactly one claim; the unique <c>lootbox_claims.LootboxTokenId</c> backs it up.
    /// Every later copy gets 409 AlreadyRedeemed and the plugin removes it.
    /// </para>
    /// </summary>
    public partial class LootboxRuntimeService
    {
        // ===== Issue =====

        public async Task<LootboxTokenIssueResultDto> IssueTokensAsync(LootboxTokenIssueRequestDto request, int? actorUserId)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.UserId <= 0) throw new ArgumentException("userId is required.", nameof(request));
            if (request.TypeId <= 0) throw new ArgumentException("typeId is required.", nameof(request));
            if (request.Quantity < 1 || request.Quantity > LootboxRuntimeServiceConstants.MaxTokensPerIssue)
                throw new ArgumentException($"quantity must be 1-{LootboxRuntimeServiceConstants.MaxTokensPerIssue}.", nameof(request));
            var reason = ParseReason(request.Reason);
            var note = Blank(request.Note);
            if (note is { Length: > LootboxRuntimeServiceConstants.MaxTokenNoteLength })
                throw new ArgumentException($"note can be at most {LootboxRuntimeServiceConstants.MaxTokenNoteLength} characters.", nameof(request));
            var givenKey = Blank(request.IdempotencyKey);
            if (givenKey is { Length: > LootboxRuntimeServiceConstants.MaxTokenIssueKeyLength })
                throw new ArgumentException($"idempotencyKey can be at most {LootboxRuntimeServiceConstants.MaxTokenIssueKeyLength} characters.", nameof(request));

            if (givenKey != null)
            {
                var stored = await _repo.GetTokensByIssueKeyAsync(givenKey);
                if (stored.Count > 0) return IssueReplay(stored, request);
            }

            _ = await _users.GetByIdAsync(request.UserId) ?? throw new KeyNotFoundException($"User {request.UserId} not found.");
            var actor = await ExistingUserIdAsync(actorUserId);
            var type = await _repo.GetTypeAsync(request.TypeId)
                ?? throw new KeyNotFoundException($"LootboxType {request.TypeId} not found.");
            if (request.BoxStars is int stars && (stars < 1 || stars > LootboxRollEngine.MaxBoxStars))
                throw new ArgumentException($"boxStars must be 1-{LootboxRollEngine.MaxBoxStars}.", nameof(request));
            // A token that can give nothing would be an item nobody can open.
            await EnsureHasLootAsync(type, request.BoxStars ?? type.MaxBoxStars);

            var key = givenKey ?? $"issue:{Guid.NewGuid():N}";
            var now = Now();
            var tokens = new List<LootboxToken>();
            for (var i = 0; i < request.Quantity; i++)
            {
                // An explicit star count, or the type's own box-grade roll per token.
                var grade = await BoxGradeAsync(type, request.BoxStars);
                tokens.Add(new LootboxToken
                {
                    Token = Guid.NewGuid(),
                    LootboxTypeId = type.Id,
                    BoxGradeId = grade.Id,
                    IssuedToUserId = request.UserId,
                    IssuedReason = reason,
                    IssueKey = key,
                    IssueIndex = i,
                    IssuedByUserId = actor,
                    Note = note,
                    IssuedAt = now,
                    Status = LootboxTokenStatus.Issued,
                });
            }

            try
            {
                await _repo.InTransactionAsync(async () =>
                {
                    _repo.AddTokens(tokens);
                    await _repo.SaveChangesAsync();
                    await AuditAsync(actor, request.UserId, AuditAction.LootboxGranted, new
                    {
                        @event = "TokensIssued",
                        reason = reason.ToString(),
                        lootboxTypeId = type.Id,
                        lootboxTypeName = type.Name,
                        boxStars = request.BoxStars,
                        quantity = tokens.Count,
                        tokenIds = tokens.Select(t => t.Id).ToList(),
                        note,
                    });
                    return tokens.Count;
                });
            }
            catch (DbUpdateException) when (givenKey != null)
            {
                // The same key in two parallel requests: the unique (IssueKey, IssueIndex) let one through.
                _repo.DiscardChanges();
                var winner = await _repo.GetTokensByIssueKeyAsync(givenKey);
                if (winner.Count == 0) throw;
                return IssueReplay(winner, request);
            }

            LootboxMetrics.TokensIssued(reason.ToString(), tokens.Count);
            _logger.LogInformation("Lootbox tokens issued ({Reason}) by user {ActorUserId}: {Count}x {Type} to user {UserId}, ids {TokenIds}",
                reason, actor, tokens.Count, type.Name, request.UserId, string.Join(",", tokens.Select(t => t.Id)));
            var issued = await _repo.GetTokensAsync(tokens.Select(t => t.Id));
            return new LootboxTokenIssueResultDto { Replay = false, Tokens = issued.Select(ToTokenDto).ToList() };
        }

        // A key names one issue: the same user, type and count get the stored tokens back; anything else is a 409.
        private LootboxTokenIssueResultDto IssueReplay(List<LootboxToken> stored, LootboxTokenIssueRequestDto request)
        {
            if (stored.Any(t => t.IssuedToUserId != request.UserId || t.LootboxTypeId != request.TypeId) || stored.Count != request.Quantity)
                throw Conflict("IdempotencyKeyReused", "That idempotency key was already used for another issue.");
            _logger.LogInformation("Lootbox token issue {IssueKey} replayed", stored[0].IssueKey);
            return new LootboxTokenIssueResultDto { Replay = true, Tokens = stored.Select(ToTokenDto).ToList() };
        }

        private static LootboxTokenReason ParseReason(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return LootboxTokenReason.Admin;
            // WorldPickup is only ever written by a pickup (it needs its world box).
            if (Enum.TryParse<LootboxTokenReason>(raw.Trim(), true, out var reason) && Enum.IsDefined(reason)
                && reason != LootboxTokenReason.WorldPickup && !int.TryParse(raw.Trim(), out _))
                return reason;
            throw new ArgumentException("reason must be Admin, PremiumTier, Kit, PvpKill, Referral or Other.");
        }

        // ===== Redeem =====

        private sealed record ClaimOutcome(int ClaimId, bool Replay);

        public async Task<LootboxClaimResultDto> RedeemTokenAsync(Guid token, LootboxTokenRedeemRequestDto request)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                return await RedeemCoreAsync(token, request);
            }
            catch (LootboxConflictException ex)
            {
                LootboxMetrics.Conflict(ex.Code);
                _logger.LogInformation("Lootbox token {Token} redeem by user {UserId} refused: {Code}", token, request?.UserId, ex.Code);
                throw;
            }
            finally
            {
                LootboxMetrics.ClaimTook(stopwatch.Elapsed.TotalMilliseconds);
            }
        }

        private async Task<LootboxClaimResultDto> RedeemCoreAsync(Guid token, LootboxTokenRedeemRequestDto request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (token == Guid.Empty) throw new ArgumentException("token is required.", nameof(token));
            if (request.UserId <= 0) throw new ArgumentException("userId is required.", nameof(request));
            var key = IdempotencyKey(request.IdempotencyKey)
                ?? throw new ArgumentException(
                    $"idempotencyKey is required (at most {LootboxRuntimeServiceConstants.MaxIdempotencyKeyLength} characters).", nameof(request));

            // An unknown token is a forged or foreign item: refused like any other dead token, but kept apart so the
            // plugin doesn't delete an item over a misconfigured API.
            var tokenId = await _repo.FindTokenIdAsync(token) ?? throw Conflict("InvalidToken", "That lootbox token doesn't exist.");

            // Fast path for a retry of the same click: hand back the stored result.
            var stored = await _repo.GetClaimByIdempotencyKeyAsync(key);
            if (stored != null) return await ReplayAsync(stored, request.UserId, null, null, tokenId);

            var user = await _users.GetByIdAsync(request.UserId)
                ?? throw new KeyNotFoundException($"User {request.UserId} not found.");
            EnsureCanOpen(user);
            var now = Now();

            ClaimOutcome outcome;
            try
            {
                outcome = await _repo.InTransactionAsync(async () =>
                {
                    // Serializes this player's redeems and pickups (daily caps, replay lookup).
                    await _users.LockUsersAsync(new[] { request.UserId });

                    var again = await _repo.GetClaimByIdempotencyKeyAsync(key);
                    if (again != null)
                    {
                        EnsureSameClaim(again, request.UserId, null, null, tokenId);
                        return new ClaimOutcome(again.Id, true);
                    }

                    var row = await _repo.GetTokenAsync(token) ?? throw Conflict("InvalidToken", "That lootbox token doesn't exist.");
                    EnsureRedeemable(row.Status);

                    var config = await _repo.GetConfigurationAsync() ?? new LootboxConfiguration();
                    if (!config.Enabled) throw Conflict("Disabled", "Lootboxes are disabled.");
                    await EnforceDailyCapAsync(request.UserId, row.LootboxType, config, now);

                    var roll = await RollAsync(row.LootboxTypeId, row.BoxGrade.Stars);

                    // Consume the token first, on its own: a racing redeem's UPDATE … WHERE Status='Issued' matches
                    // no row and fails here, before anything else is written.
                    row.Status = LootboxTokenStatus.Redeemed;
                    row.RedeemedAt = now;
                    row.RedeemedByUserId = request.UserId;
                    await _repo.SaveChangesAsync();

                    var claim = await MintAsync(roll, request.UserId, row.LootboxTypeId, row.BoxGradeId, null, key, now, row.Id);
                    return new ClaimOutcome(claim.Id, false);
                });
            }
            catch (DbUpdateConcurrencyException)
            {
                _repo.DiscardChanges();
                throw await TokenGoneAsync(tokenId);
            }
            catch (DbUpdateException ex)
            {
                // A unique index caught what the checks above didn't (the same key or token in two parallel requests).
                _repo.DiscardChanges();
                var winner = await _repo.GetClaimByIdempotencyKeyAsync(key);
                if (winner != null) return await ReplayAsync(winner, request.UserId, null, null, tokenId);
                if (await _repo.TokenHasClaimAsync(tokenId)) throw Conflict("AlreadyRedeemed", "This lootbox token was already opened.");
                _logger.LogError(ex, "Lootbox token {Token} redeem by user {UserId} failed to save", token, request.UserId);
                throw;
            }

            var result = await ResultAsync(outcome.ClaimId, outcome.Replay)
                ?? throw new InvalidOperationException($"Lootbox claim {outcome.ClaimId} vanished.");
            if (outcome.Replay)
            {
                _logger.LogInformation("Lootbox claim {ClaimId} (token {Token}) replayed for user {UserId}", result.ClaimId, token, result.UserId);
            }
            else
            {
                LootboxMetrics.Claimed(TypeLabel(result), result.BoxStars, result.ItemGradeStars, result.IsSpecial);
                _logger.LogInformation(
                    "Lootbox claim {ClaimId}: user {UserId} redeemed token {TokenId} (★{BoxStars}) and got blueprint {BlueprintId} ★{ItemStars} x{Quantity}, instance {InstanceId}, special {IsSpecial}",
                    result.ClaimId, result.UserId, tokenId, result.BoxStars, result.ItemBlueprintId, result.ItemGradeStars,
                    result.Quantity, result.ItemInstanceId, result.IsSpecial);
            }
            return result;
        }

        private static void EnsureRedeemable(LootboxTokenStatus status)
        {
            switch (status)
            {
                case LootboxTokenStatus.Redeemed:
                    throw Conflict("AlreadyRedeemed", "This lootbox token was already opened.");
                case LootboxTokenStatus.Revoked:
                    throw Conflict("Revoked", "This lootbox token was revoked.");
            }
        }

        private async Task<LootboxConflictException> TokenGoneAsync(int tokenId)
        {
            return await _repo.GetTokenStatusAsync(tokenId) == LootboxTokenStatus.Revoked
                ? Conflict("Revoked", "This lootbox token was revoked.")
                : Conflict("AlreadyRedeemed", "This lootbox token was already opened.");
        }

        // ===== Delivery, revoke, reads =====

        public async Task<List<LootboxTokenDto>> GetUndeliveredTokensAsync(int userId)
        {
            if (userId <= 0) throw new ArgumentException("userId is required.", nameof(userId));
            return (await _repo.GetUndeliveredTokensAsync(userId)).Select(ToTokenDto).ToList();
        }

        public async Task<LootboxTokensDeliveredResultDto> MarkTokensDeliveredAsync(LootboxTokensDeliveredRequestDto request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.UserId <= 0) throw new ArgumentException("userId is required.", nameof(request));
            var tokens = (request.Tokens ?? new List<Guid>()).Where(t => t != Guid.Empty).Distinct().ToList();
            if (tokens.Count == 0) throw new ArgumentException("tokens is required.", nameof(request));
            if (tokens.Count > LootboxRuntimeServiceConstants.MaxTokensPerIssue)
                throw new ArgumentException($"At most {LootboxRuntimeServiceConstants.MaxTokensPerIssue} tokens per call.", nameof(request));

            var rows = await _repo.GetTokensForDeliveryAsync(request.UserId, tokens);
            if (rows.Count == 0) return new LootboxTokensDeliveredResultDto { Updated = 0 };
            var now = Now();
            foreach (var row in rows) row.DeliveredAt = now;
            // DeliveredAt is not a concurrency token: two confirmations of the same token just both set it.
            await _repo.SaveChangesAsync();
            _logger.LogInformation("Lootbox tokens {TokenIds} delivered to user {UserId}", string.Join(",", rows.Select(r => r.Id)), request.UserId);
            return new LootboxTokensDeliveredResultDto { Updated = rows.Count };
        }

        public async Task<LootboxTokenDto> RevokeTokenAsync(Guid token, int? actorUserId)
        {
            if (token == Guid.Empty) throw new ArgumentException("token is required.", nameof(token));
            var row = await _repo.GetTokenAsync(token) ?? throw new KeyNotFoundException("Lootbox token not found.");
            if (row.Status == LootboxTokenStatus.Redeemed) throw Conflict("AlreadyRedeemed", "This lootbox token was already opened.");
            if (row.Status == LootboxTokenStatus.Issued)
            {
                var actor = await ExistingUserIdAsync(actorUserId);
                row.Status = LootboxTokenStatus.Revoked;
                row.RevokedAt = Now();
                try
                {
                    await _repo.InTransactionAsync(async () =>
                    {
                        await _repo.SaveChangesAsync();
                        await AuditAsync(actor, row.IssuedToUserId, AuditAction.LootboxGranted, new
                        {
                            @event = "TokenRevoked",
                            tokenId = row.Id,
                            lootboxTypeId = row.LootboxTypeId,
                            boxStars = row.BoxGrade?.Stars,
                        });
                        return row.Id;
                    });
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Redeemed in the meantime.
                    _repo.DiscardChanges();
                    throw await TokenGoneAsync(row.Id);
                }
                _logger.LogInformation("Lootbox token {TokenId} revoked by user {ActorUserId}", row.Id, actor);
                // Every online copy is taken away at once; offline holders lose it at their next join (plugin scan).
                NotifyWorldChanged(null, new[] { row.Token });
            }
            return ToTokenDto((await _repo.GetTokensAsync(new[] { row.Id })).Single());
        }

        public async Task<PagedResultDto<LootboxTokenDto>> SearchTokensAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));
            var query = new PagedQuery
            {
                PageNumber = Math.Max(1, queryDto.PageNumber),
                PageSize = Math.Clamp(queryDto.PageSize, 1, 200),
                SearchTerm = queryDto.SearchTerm,
                SortBy = queryDto.SortBy,
                SortDescending = queryDto.SortDescending,
                Filters = queryDto.Filters,
            };
            var result = await _repo.SearchTokensAsync(query);
            return new PagedResultDto<LootboxTokenDto>
            {
                Items = result.Items.Select(ToTokenDto).ToList(),
                TotalCount = result.TotalCount,
                PageNumber = result.PageNumber,
                PageSize = result.PageSize,
            };
        }

        private static LootboxTokenDto ToTokenDto(LootboxToken token) => new()
        {
            Id = token.Id,
            Token = token.Token,
            LootboxTypeId = token.LootboxTypeId,
            LootboxTypeName = token.LootboxType?.Name ?? string.Empty,
            CategoryName = token.LootboxType?.Category?.Name,
            BoxGradeId = token.BoxGradeId,
            BoxStars = token.BoxGrade?.Stars ?? 0,
            BoxLabel = BoxLabel(token.BoxGrade, token.LootboxType),
            Status = token.Status.ToString(),
            Reason = token.IssuedReason.ToString(),
            Note = token.Note,
            IssuedToUserId = token.IssuedToUserId,
            IssuedToUsername = token.IssuedToUser?.Username,
            IssuedByUserId = token.IssuedByUserId,
            IssuedAt = Utc(token.IssuedAt),
            DeliveredAt = Utc(token.DeliveredAt),
            RedeemedAt = Utc(token.RedeemedAt),
            RedeemedByUserId = token.RedeemedByUserId,
            RedeemedByUsername = token.RedeemedByUser?.Username,
            RevokedAt = Utc(token.RevokedAt),
            ClaimId = token.Claim?.Id,
            SourceSpawnId = token.SourceSpawnId,
        };
    }
}
