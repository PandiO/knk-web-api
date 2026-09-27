using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Lootbox;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Picking up a world box (docs/specs/lootboxes/DESIGN.md §3.8, smoke test 2026-09-27): the first player to click a
    /// spawned box takes it as a token item instead of opening it on the spot, and opens it later like any other token
    /// (the same roll, daily open cap and ItemInstance mint, in the redeem).
    /// <para>
    /// One transaction: the player's row lock (their pickups are counted under it), the box flipped to Claimed through
    /// its <c>[ConcurrencyCheck]</c> Status (a racing picker matches no row and gets 409 AlreadyClaimed), then one
    /// <see cref="LootboxToken"/> of the box's type and grade (reason WorldPickup, <c>SourceSpawnId</c> unique). A repeat
    /// by the same player returns their stored token, so a timed-out click can't lose the box.
    /// </para>
    /// <para>
    /// The daily cap (<c>MaxClaimsPerPlayerPerDay</c>, global and per type) applies to pickups as well as to opens: the
    /// cap still limits how many world boxes one player can sweep up in a UTC day, as it did when a click opened the box.
    /// </para>
    /// </summary>
    public partial class LootboxRuntimeService
    {
        public async Task<LootboxPickupResultDto> PickupAsync(int spawnId, LootboxPickupRequestDto request)
        {
            try
            {
                return await PickupCoreAsync(spawnId, request);
            }
            catch (LootboxConflictException ex)
            {
                LootboxMetrics.Conflict(ex.Code);
                _logger.LogInformation("Lootbox pickup of spawn {SpawnId} by user {UserId} refused: {Code}", spawnId, request?.UserId, ex.Code);
                throw;
            }
        }

        private async Task<LootboxPickupResultDto> PickupCoreAsync(int spawnId, LootboxPickupRequestDto request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (spawnId <= 0) throw new ArgumentException("Invalid spawn id.", nameof(spawnId));
            if (request.UserId <= 0) throw new ArgumentException("userId is required.", nameof(request));

            var now = Now();
            await _repo.ExpireDueAsync(now);

            // Fast path for a retry of the same click: no lock needed to hand back the stored token.
            var existing = await _repo.GetTokenBySourceSpawnAsync(spawnId);
            if (existing != null) return PickupReplay(existing, spawnId, request.UserId);

            var user = await _users.GetByIdAsync(request.UserId)
                ?? throw new KeyNotFoundException($"User {request.UserId} not found.");
            EnsureCanOpen(user);

            int tokenId;
            try
            {
                tokenId = await _repo.InTransactionAsync(async () =>
                {
                    // Serializes this player's pickups, claims and redeems: the daily counts see what a parallel one committed.
                    await _users.LockUsersAsync(new[] { request.UserId });

                    var spawn = await _repo.GetSpawnAsync(spawnId)
                        ?? throw new KeyNotFoundException($"Lootbox spawn {spawnId} not found.");
                    if (spawn.Token != request.Token) throw Conflict("TokenMismatch", "The lootbox token doesn't match.");
                    EnsureClaimable(spawn.Status, spawn.ExpiresAt, now);

                    var config = await _repo.GetConfigurationAsync() ?? new LootboxConfiguration();
                    if (!config.Enabled) throw Conflict("Disabled", "Lootboxes are disabled.");
                    await EnforceDailyPickupCapAsync(request.UserId, spawn.LootboxType, config, now);

                    // Flip the box first, on its own: a racing picker's UPDATE … WHERE Status='Active' matches no row.
                    spawn.Status = LootboxSpawnStatus.Claimed;
                    spawn.ClaimedAt = now;
                    spawn.ClaimedByUserId = request.UserId;
                    await _repo.SaveChangesAsync();

                    var token = new LootboxToken
                    {
                        Token = Guid.NewGuid(),
                        LootboxTypeId = spawn.LootboxTypeId,
                        BoxGradeId = spawn.BoxGradeId,
                        IssuedToUserId = request.UserId,
                        IssuedReason = LootboxTokenReason.WorldPickup,
                        IssueKey = $"pickup:{spawn.Token:N}",
                        IssueIndex = 0,
                        SourceSpawnId = spawn.Id,
                        IssuedAt = now,
                        Status = LootboxTokenStatus.Issued,
                    };
                    _repo.AddTokens(new[] { token });
                    await _repo.SaveChangesAsync();
                    return token.Id;
                });
            }
            catch (DbUpdateConcurrencyException)
            {
                _repo.DiscardChanges();
                throw await SpawnGoneAsync(spawnId);
            }
            catch (DbUpdateException ex)
            {
                // The unique SourceSpawnId / IssueKey caught a second pickup of the same box.
                _repo.DiscardChanges();
                var winner = await _repo.GetTokenBySourceSpawnAsync(spawnId);
                if (winner != null) return PickupReplay(winner, spawnId, request.UserId);
                _logger.LogError(ex, "Lootbox pickup of spawn {SpawnId} by user {UserId} failed to save", spawnId, request.UserId);
                throw;
            }

            var issued = (await _repo.GetTokensAsync(new[] { tokenId })).Single();
            LootboxMetrics.TokensIssued(LootboxTokenReason.WorldPickup.ToString(), 1);
            _logger.LogInformation("Lootbox spawn {SpawnId} (★{BoxStars} {Type}) picked up by user {UserId} as token {TokenId}",
                spawnId, issued.BoxGrade?.Stars, issued.LootboxType?.Name, request.UserId, tokenId);
            return new LootboxPickupResultDto { Replay = false, SpawnId = spawnId, LootboxToken = ToTokenDto(issued) };
        }

        // The same player again (a retried click): their token. Anyone else: the box is taken.
        private LootboxPickupResultDto PickupReplay(LootboxToken stored, int spawnId, int userId)
        {
            if (stored.IssuedToUserId != userId) throw Conflict("AlreadyClaimed", "Someone else got there first.");
            _logger.LogInformation("Lootbox pickup of spawn {SpawnId} replayed for user {UserId}", spawnId, userId);
            return new LootboxPickupResultDto { Replay = true, SpawnId = spawnId, LootboxToken = ToTokenDto(stored) };
        }

        private async Task<LootboxConflictException> SpawnGoneAsync(int spawnId)
        {
            return await _repo.GetSpawnStatusAsync(spawnId) switch
            {
                LootboxSpawnStatus.Expired => Conflict("Expired", "This lootbox has expired."),
                LootboxSpawnStatus.Removed => Conflict("Removed", "This lootbox was removed."),
                _ => Conflict("AlreadyClaimed", "Someone else got there first."),
            };
        }

        /// <summary>The UTC-day cap on world boxes picked up: all types together first, then this type.</summary>
        private async Task EnforceDailyPickupCapAsync(int userId, LootboxType type, LootboxConfiguration config, DateTime now)
        {
            var (dayStart, dayEnd) = UtcDay(now);
            if (config.MaxClaimsPerPlayerPerDay is int globalLimit
                && await _repo.CountPickupsAsync(userId, dayStart, dayEnd) >= globalLimit)
            {
                throw new LootboxDailyLimitException(LootboxDailyLimitException.GlobalScope, globalLimit, dayEnd, LootboxDailyLimitException.PickupCode);
            }
            if (type.MaxClaimsPerPlayerPerDay is int typeLimit
                && await _repo.CountPickupsAsync(userId, dayStart, dayEnd, type.Id) >= typeLimit)
            {
                throw new LootboxDailyLimitException(LootboxDailyLimitException.TypeScope, typeLimit, dayEnd, LootboxDailyLimitException.PickupCode);
            }
        }

        // ===== Token status (the plugin's join scan) =====

        public async Task<List<LootboxTokenStatusDto>> GetTokenStatusesAsync(LootboxTokenStatusRequestDto request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var tokens = (request.Tokens ?? new List<Guid>()).Where(t => t != Guid.Empty).Distinct().ToList();
            if (tokens.Count > LootboxRuntimeServiceConstants.MaxTokenStatusLookup)
                throw new ArgumentException($"At most {LootboxRuntimeServiceConstants.MaxTokenStatusLookup} tokens per call.", nameof(request));
            if (tokens.Count == 0) return new List<LootboxTokenStatusDto>();
            var statuses = await _repo.GetTokenStatusesAsync(tokens);
            return tokens.Select(t => new LootboxTokenStatusDto
            {
                Token = t,
                Status = statuses.TryGetValue(t, out var status) ? status.ToString() : "Unknown",
            }).ToList();
        }

        // ===== Server-wide changes for the game server (LootboxWorldChanged) =====

        /// <summary>Tells the game server about boxes that are gone and tokens that were revoked, so it applies them
        /// within seconds instead of at its next runtime refresh.</summary>
        private void NotifyWorldChanged(IEnumerable<int>? removedSpawnIds, IEnumerable<Guid>? revokedTokens)
        {
            var change = new LootboxWorldChangedNotificationDto
            {
                RemovedSpawnIds = removedSpawnIds?.Distinct().ToList() ?? new List<int>(),
                RevokedTokens = revokedTokens?.Distinct().ToList() ?? new List<Guid>(),
            };
            if (_notifications == null || (change.RemovedSpawnIds.Count == 0 && change.RevokedTokens.Count == 0)) return;
            _notifications.EnqueueLootboxWorldChanged(change);
        }

        /// <summary>For services outside this class (a web area delete).</summary>
        public void NotifySpawnsRemoved(IEnumerable<int> spawnIds) => NotifyWorldChanged(spawnIds, null);
    }
}
