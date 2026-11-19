using System;
using System.Collections.Generic;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Services.Stock
{
    /// <summary>
    /// One line of a stock request: <see cref="Amount"/> of a blueprint (<see cref="ItemBlueprintId"/>), or one unique
    /// item (<see cref="ItemInstanceId"/>, amount 1), from one storage to another (production DESIGN § 4.5).
    /// </summary>
    public sealed record StockLineRequest(int FromStorageId, int ToStorageId, int? ItemBlueprintId, long Amount,
        long? ItemInstanceId = null)
    {
        public static StockLineRequest Fungible(int from, int to, int blueprintId, long amount) => new(from, to, blueprintId, amount);

        public static StockLineRequest Unique(int from, int to, long itemInstanceId) => new(from, to, null, 1, itemInstanceId);
    }

    /// <summary>Who asks and why. One reason per transaction; the idempotency key is unique across the ledger.</summary>
    public sealed class StockContext
    {
        public StockReason Reason { get; init; }
        public StockActorKind ActorKind { get; init; } = StockActorKind.System;
        public int? ActorUserId { get; init; }
        public string? ActorComponent { get; init; }
        public string? SourceType { get; init; }
        public string? SourceRef { get; init; }
        public string? Note { get; init; }
        public string? IdempotencyKey { get; init; }

        public static StockContext ForSystem(string component, StockReason reason, string? idempotencyKey = null) =>
            new() { Reason = reason, ActorKind = StockActorKind.System, ActorComponent = component, IdempotencyKey = idempotencyKey };
    }

    /// <summary>What happened to one request line. <c>Requested = Accepted + Rejected</c>.</summary>
    public sealed record StockLineResult(int LineNo, int FromStorageId, int ToStorageId, int? ItemBlueprintId,
        long? ItemInstanceId, long Requested, long Accepted, long Rejected, StockRejectReason RejectReason);

    /// <summary>
    /// The result of a stock request. Capacity, missing stock and locks are reported here, never thrown (V2 defects 1,
    /// 2, 4). <see cref="TransactionId"/> is null when nothing moved. <see cref="Replayed"/> = the idempotency key was
    /// seen before and this is the first request's applied result (a replay reports what was applied; rejected parts
    /// are not stored, so their reason reads <see cref="StockRejectReason.None"/>).
    /// </summary>
    public sealed class StockResult
    {
        public StockResultStatus Status { get; init; }
        public long? TransactionId { get; init; }
        public bool Replayed { get; init; }
        public IReadOnlyList<StockLineResult> Lines { get; init; } = Array.Empty<StockLineResult>();

        public long Accepted
        {
            get
            {
                long sum = 0;
                foreach (var l in Lines) sum += l.Accepted;
                return sum;
            }
        }
    }

    /// <summary>The same idempotency key came with a different request (409); a real retry sends the same request.</summary>
    public sealed class StockIdempotencyConflictException : InvalidOperationException
    {
        public StockIdempotencyConflictException(string key)
            : base($"Idempotency key '{key}' was already used for a different stock request.") { }
    }
}
