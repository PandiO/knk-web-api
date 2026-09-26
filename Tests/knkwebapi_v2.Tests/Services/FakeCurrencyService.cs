using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// ICurrencyService for unit tests whose repositories are mocks (no DbContext for the real
/// CurrencyService): applies postings straight onto the User objects the test's repository mock
/// hands out, with the ledger's bounds, direction and replay-by-key rules, and records every
/// posting so a test can assert the reason, key and amounts. The real ledger is covered by
/// CurrencyServiceTests (InMemory) and Tests/MySql.
/// </summary>
public sealed class FakeCurrencyService : ICurrencyService
{
    private readonly Func<int, User?> _users;
    private readonly Dictionary<(string Scope, string Key), PostingResult> _byKey = new();
    private long _nextId = 1;

    public FakeCurrencyService(Func<int, User?> users)
    {
        _users = users;
    }

    /// <summary>Every posting made (replays excluded), in order.</summary>
    public List<(IReadOnlyList<CurrencyLeg> Legs, CurrencyContext Ctx, PostingResult Result)> Postings { get; } = new();

    public Dictionary<Currency, CurrencyPolicy> Policies { get; } = new();

    public Task<PostingResult> PostAsync(IReadOnlyList<CurrencyLeg> legs, CurrencyContext ctx, CancellationToken ct = default)
    {
        var reason = CurrencyReasons.Find(ctx.ReasonCode) ?? throw new CurrencyException(CurrencyErrorCode.InvalidRequest, $"Unknown reason {ctx.ReasonCode}.");
        foreach (var leg in legs)
        {
            if (leg.Amount == 0 || Math.Abs(leg.Amount) > CurrencyService.Cap(leg.Currency))
                throw new CurrencyException(CurrencyErrorCode.AmountOutOfRange, $"Leg amount {leg.Amount}.");
            if (reason.Direction == CurrencyReasonDirection.Credit && leg.Amount < 0 || reason.Direction == CurrencyReasonDirection.Debit && leg.Amount > 0)
                throw new CurrencyException(CurrencyErrorCode.AmountOutOfRange, $"{reason.Code} leg in the wrong direction.");
        }
        return Task.FromResult(Apply(legs.Select(l => (l.UserId, l.Currency, l.Amount, l.Amount < 0 ? CurrencyOperation.Remove : CurrencyOperation.Add)).ToList(), ctx));
    }

    public Task<PostingResult> GrantAsync(int userId, Currency currency, long amount, CurrencyContext ctx, CancellationToken ct = default) =>
        PostAsync(new[] { new CurrencyLeg(userId, currency, amount) }, ctx, ct);

    public Task<PostingResult> SpendAsync(int userId, Currency currency, long amount, CurrencyContext ctx, CancellationToken ct = default) =>
        PostAsync(new[] { new CurrencyLeg(userId, currency, -amount) }, ctx, ct);

    public Task<PostingResult> AdminAdjustAsync(AdminAdjustRequest req, CurrencyContext ctx, CancellationToken ct = default)
    {
        if (ctx.ReasonCode != CurrencyReasons.ForAdminMode(req.Mode))
            throw new CurrencyException(CurrencyErrorCode.InvalidRequest, "Wrong admin reason code.");
        if (req.Amount < (req.Mode == CurrencyOperation.Set ? 0 : 1) || req.Amount > CurrencyService.Cap(req.Currency))
            throw new CurrencyException(CurrencyErrorCode.AmountOutOfRange, $"Amount {req.Amount}.");
        if (_byKey.TryGetValue((ctx.IdempotencyScope, ctx.IdempotencyKey), out var stored))
            return Task.FromResult(Replay(stored));
        var user = _users(req.UserId) ?? throw new CurrencyException(CurrencyErrorCode.UserNotFound, "No such user.");
        var current = Balance(user, req.Currency);
        if (req.ExpectedCurrent.HasValue && req.ExpectedCurrent != current)
            throw new CurrencyException(CurrencyErrorCode.ExpectedBalanceMismatch, "Stale expected balance.");
        var delta = req.Mode switch
        {
            CurrencyOperation.Add => req.Amount,
            CurrencyOperation.Remove => -req.Amount,
            _ => req.Amount - current
        };
        return Task.FromResult(Apply(new List<(int, Currency, long, CurrencyOperation)> { (req.UserId, req.Currency, delta, req.Mode) }, ctx));
    }

    public Task<PostingResult> ReverseAsync(long transactionId, ReversalOptions opts, CurrencyContext ctx, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<PostingResult?> FindAsync(string scope, string idempotencyKey, CancellationToken ct = default) =>
        Task.FromResult(_byKey.TryGetValue((scope, idempotencyKey), out var stored) ? Replay(stored) : null);

    public Task<IReadOnlyDictionary<Currency, CurrencyPolicy>> GetPoliciesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Currency, CurrencyPolicy>>(Policies);

    public Task<BalancesDto> GetBalancesAsync(int userId, CancellationToken ct = default)
    {
        var user = _users(userId) ?? throw new CurrencyException(CurrencyErrorCode.UserNotFound, "No such user.");
        return Task.FromResult(new BalancesDto { UserId = userId, Coins = user.Coins, Gems = user.Gems, ExperiencePoints = user.ExperiencePoints });
    }

    public Task<PagedResultDto<LedgerLineDto>> GetHistoryAsync(LedgerQuery q, CancellationToken ct = default) =>
        throw new NotSupportedException();

    private PostingResult Apply(List<(int UserId, Currency Currency, long Amount, CurrencyOperation Op)> legs, CurrencyContext ctx)
    {
        if (_byKey.TryGetValue((ctx.IdempotencyScope, ctx.IdempotencyKey), out var stored))
            return Replay(stored);

        // Check every leg before touching any user, like the ledger.
        var planned = new List<(User User, Currency Currency, long Amount, CurrencyOperation Op, long Before, long After)>();
        foreach (var leg in legs)
        {
            var user = _users(leg.UserId) ?? throw new CurrencyException(CurrencyErrorCode.UserNotFound, "No such user.");
            var before = Balance(user, leg.Currency);
            var after = before + leg.Amount;
            if (after < 0)
                throw new CurrencyException(CurrencyErrorCode.InsufficientFunds, $"User {user.Id} has {before} {leg.Currency}.");
            if (after > CurrencyService.Cap(leg.Currency))
                throw new CurrencyException(CurrencyErrorCode.BalanceCapExceeded, $"User {user.Id} over the {leg.Currency} cap.");
            planned.Add((user, leg.Currency, leg.Amount, leg.Op, before, after));
        }
        foreach (var p in planned)
        {
            switch (p.Currency)
            {
                case Currency.Coins: p.User.Coins = (int)p.After; break;
                case Currency.Gems: p.User.Gems = (int)p.After; break;
                default: p.User.ExperiencePoints = (int)p.After; break;
            }
        }

        var id = _nextId++;
        var result = new PostingResult
        {
            TransactionId = id,
            PublicId = $"FAKE{id:D22}",
            ReasonCode = ctx.ReasonCode,
            CreatedAt = DateTime.UtcNow,
            Entries = planned.Select(p => new PostedEntryDto
            {
                UserId = p.User.Id,
                Currency = p.Currency.ToString(),
                Operation = p.Op.ToString(),
                Amount = p.Amount,
                BalanceBefore = p.Before,
                BalanceAfter = p.After
            }).ToList(),
            Balances = planned.Select(p => p.User).Distinct().ToDictionary(u => u.Id,
                u => new BalancesDto { UserId = u.Id, Coins = u.Coins, Gems = u.Gems, ExperiencePoints = u.ExperiencePoints })
        };
        _byKey[(ctx.IdempotencyScope, ctx.IdempotencyKey)] = result;
        Postings.Add((legs.Select(l => new CurrencyLeg(l.UserId, l.Currency, l.Amount)).ToList(), ctx, result));
        return result;
    }

    private static PostingResult Replay(PostingResult stored) => new()
    {
        TransactionId = stored.TransactionId,
        PublicId = stored.PublicId,
        ReasonCode = stored.ReasonCode,
        CreatedAt = stored.CreatedAt,
        Entries = stored.Entries,
        Balances = stored.Balances,
        Replayed = true
    };

    private static long Balance(User user, Currency currency) => currency switch
    {
        Currency.Coins => user.Coins,
        Currency.Gems => user.Gems,
        _ => user.ExperiencePoints
    };
}
