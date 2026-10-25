using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.DependencyInjection;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Invitations;

namespace knkwebapi_v2.Tests.Services.Invitations;

/// <summary>KNG-212: the real service registration resolves the invitation service (and AuthService with
/// it), and binds the Invitations section and Security:Registration:Mode.</summary>
public class InvitationRegistrationDiTests
{
    [Fact]
    public void ApplicationServices_ResolveTheInvitationService_AndBindItsSettings()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Invitations:MaxExpiryDays"] = "14",
            ["Security:Registration:Mode"] = "Invite",
            ["Security:Jwt:Secret"] = "test-only-jwt-secret-0123456789abcdef-0123456789",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDbContext<KnKDbContext>(o => o.UseInMemoryDatabase("di-" + Guid.NewGuid()));
        services.Configure<SecuritySettings>(configuration.GetSection("Security"));
        services.AddApplicationServices(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<InvitationService>(scope.ServiceProvider.GetRequiredService<IInvitationService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAuthService>());
        Assert.Equal(14, scope.ServiceProvider.GetRequiredService<IOptions<InvitationSettings>>().Value.MaxExpiryDays);
        Assert.Equal(RegistrationMode.Invite, scope.ServiceProvider.GetRequiredService<IOptions<SecuritySettings>>().Value.Registration.Mode);
    }

    [Fact]
    public void ApplicationServices_ResolveTheAdmissionService_AndBindItsSettings()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Admission:ClaimCodeMinutes"] = "10",
            ["Security:Jwt:Secret"] = "test-only-jwt-secret-0123456789abcdef-0123456789",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDbContext<KnKDbContext>(o => o.UseInMemoryDatabase("di-" + Guid.NewGuid()));
        services.Configure<SecuritySettings>(configuration.GetSection("Security"));
        services.AddApplicationServices(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<AdmissionService>(scope.ServiceProvider.GetRequiredService<IAdmissionService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUserService>());
        Assert.Equal(10, scope.ServiceProvider.GetRequiredService<IOptions<AdmissionSettings>>().Value.ClaimCodeMinutes);
    }

    [Fact]
    public void ApplicationServices_ResolveTheCloudflareAccessSync_OffByDefault()
    {
        // KNG-216: registered everywhere, but SyncEnabled defaults to false (behaviour-neutral).
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CloudflareAccess:ReconcileMinutes"] = "7",
            ["Security:Jwt:Secret"] = "test-only-jwt-secret-0123456789abcdef-0123456789",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDbContext<KnKDbContext>(o => o.UseInMemoryDatabase("di-" + Guid.NewGuid()));
        services.Configure<SecuritySettings>(configuration.GetSection("Security"));
        services.AddApplicationServices(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var sync = scope.ServiceProvider.GetRequiredService<knkwebapi_v2.Services.CloudflareAccess.ICloudflareAccessSync>();
        Assert.False(sync.Enabled);
        Assert.Equal(7, sync.GetStatus().ReconcileMinutes);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAuthService>());
        Assert.Contains(provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>(), h => h is knkwebapi_v2.Services.CloudflareAccess.CloudflareAccessSyncService);
    }
}
