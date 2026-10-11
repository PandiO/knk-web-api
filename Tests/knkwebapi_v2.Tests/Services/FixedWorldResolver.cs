using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-111: a world resolver for tests that aren't about worlds. Every domain resolves to <see cref="World"/>; the
/// real rules are covered by DomainWorldResolverTests.
/// </summary>
public sealed class FixedWorldResolver : IDomainWorldResolver
{
    public const string World = "world";

    public static readonly FixedWorldResolver Instance = new();

    public Task<string> ResolveAsync(DomainWorldRequest request) =>
        Task.FromResult(string.IsNullOrWhiteSpace(request.RequestedWorld) ? World : request.RequestedWorld.Trim());

    public Task<DomainWorldResolution> TryResolveAsync(DomainWorldRequest request) =>
        Task.FromResult(new DomainWorldResolution(World, "test", null, null));
}
