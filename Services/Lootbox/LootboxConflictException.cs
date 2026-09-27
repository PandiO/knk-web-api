namespace knkwebapi_v2.Services.Lootbox;

/// <summary>
/// A lootbox request that conflicts with existing state; controllers answer 409 <c>{ code, message }</c>
/// (docs/specs/lootboxes/DESIGN.md §3.3, e.g. <c>CategoryTaken</c>, <c>NameTaken</c>, <c>InUse</c>).
/// </summary>
public class LootboxConflictException : InvalidOperationException
{
    public string Code { get; }

    public LootboxConflictException(string code, string message) : base(message)
    {
        Code = code;
    }
}
