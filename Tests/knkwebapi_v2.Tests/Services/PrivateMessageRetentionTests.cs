using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-18 Phase 3: private messages are deleted after
/// AuditLogRetentionConfiguration.PrivateMessageRetentionDays (default 30, DESIGN.md §3.1) by
/// RetentionPolicyService's own cleanup block, re-reading the setting every run and running even
/// when another cleanup fails. The SQL delete itself (ExecuteDeleteAsync, which the in-memory
/// provider can't run) was checked against MySQL.
/// </summary>
public class PrivateMessageRetentionTests
{
    private readonly Mock<IPrivateMessageLogRepository> _pmLog = new();
    private readonly Mock<IAuditLogRepository> _auditLog = new();
    private readonly Mock<IFormSubmissionProgressRepository> _forms = new();
    private readonly Mock<IAuditLogRetentionConfigurationService> _config = new();
    private readonly List<DateTime> _pmCutoffs = new();
    private int _pmRetentionDays = 30;

    public PrivateMessageRetentionTests()
    {
        _config.Setup(c => c.GetAsync()).ReturnsAsync(() => new AuditLogRetentionConfigurationDto
        {
            RetentionDays = 180,
            PrivateMessageRetentionDays = _pmRetentionDays
        });
        _pmLog.Setup(r => r.DeleteOlderThanAsync(It.IsAny<DateTime>()))
            .Callback<DateTime>(cutoff => { lock (_pmCutoffs) _pmCutoffs.Add(cutoff); })
            .ReturnsAsync(4);
    }

    private RetentionPolicyService NewService(TimeSpan interval)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_pmLog.Object);
        services.AddSingleton(_auditLog.Object);
        services.AddSingleton(_forms.Object);
        services.AddSingleton(_config.Object);
        return new RetentionPolicyService(services.BuildServiceProvider(), NullLogger<RetentionPolicyService>.Instance,
            runInterval: interval);
    }

    private async Task WaitForRunsAsync(int runs)
    {
        for (var i = 0; i < 200; i++)
        {
            lock (_pmCutoffs) if (_pmCutoffs.Count >= runs) return;
            await Task.Delay(20);
        }
        Assert.Fail($"Expected {runs} private message cleanup run(s), saw {_pmCutoffs.Count}.");
    }

    [Fact]
    public async Task Startup_DeletesMessagesOlderThanTheConfiguredDays()
    {
        var service = NewService(TimeSpan.FromHours(24));
        var before = DateTime.UtcNow;

        await service.StartAsync(CancellationToken.None);
        await WaitForRunsAsync(1);
        await service.StopAsync(CancellationToken.None);

        var cutoff = _pmCutoffs[0];
        Assert.InRange(cutoff, before.AddDays(-30).AddSeconds(-1), DateTime.UtcNow.AddDays(-30).AddSeconds(1));
    }

    [Fact]
    public async Task EachRun_RereadsTheSetting()
    {
        var service = NewService(TimeSpan.FromMilliseconds(100));

        await service.StartAsync(CancellationToken.None);
        await WaitForRunsAsync(1);
        _pmRetentionDays = 7;
        await WaitForRunsAsync(3);
        await service.StopAsync(CancellationToken.None);

        var last = _pmCutoffs[^1];
        Assert.InRange(last, DateTime.UtcNow.AddDays(-7).AddMinutes(-1), DateTime.UtcNow.AddDays(-7).AddSeconds(1));
    }

    [Fact]
    public async Task RunsEvenWhenTheOtherCleanupsFail()
    {
        _forms.Setup(r => r.DeleteCompletedOlderThanAsync(It.IsAny<DateTime>())).ThrowsAsync(new InvalidOperationException("boom"));
        _auditLog.Setup(r => r.DeleteOlderThanAsync(It.IsAny<DateTime>())).ThrowsAsync(new InvalidOperationException("boom"));
        var service = NewService(TimeSpan.FromHours(24));

        await service.StartAsync(CancellationToken.None);
        await WaitForRunsAsync(1);
        await service.StopAsync(CancellationToken.None);
    }

    // ===== The setting =====

    private static (AuditLogRetentionConfigurationService service, Func<AuditLogRetentionConfiguration?> stored) ConfigService(
        AuditLogRetentionConfiguration? existing)
    {
        var repo = new Mock<IAuditLogRetentionConfigurationRepository>();
        var row = existing;
        repo.Setup(r => r.GetSingletonAsync()).ReturnsAsync(() => row);
        repo.Setup(r => r.UpsertAsync(It.IsAny<AuditLogRetentionConfiguration>()))
            .ReturnsAsync((AuditLogRetentionConfiguration c) => row = c);
        return (new AuditLogRetentionConfigurationService(repo.Object), () => row);
    }

    [Fact]
    public async Task Setting_DefaultsTo30()
    {
        var (service, _) = ConfigService(null);
        Assert.Equal(30, (await service.GetAsync()).PrivateMessageRetentionDays);
    }

    [Fact]
    public async Task Setting_UpdateLeavesItAlone_WhenNotSent()
    {
        var (service, stored) = ConfigService(new AuditLogRetentionConfiguration { RetentionDays = 180, PrivateMessageRetentionDays = 14 });

        await service.UpdateAsync(new UpdateAuditLogRetentionConfigurationDto { RetentionDays = 90 });
        Assert.Equal((90, 14), (stored()!.RetentionDays, stored()!.PrivateMessageRetentionDays));

        var dto = await service.UpdateAsync(new UpdateAuditLogRetentionConfigurationDto { RetentionDays = 90, PrivateMessageRetentionDays = 7 });
        Assert.Equal(7, dto.PrivateMessageRetentionDays);
    }

    [Fact]
    public async Task Setting_BelowOneDay_IsRefused()
    {
        var (service, _) = ConfigService(null);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateAsync(new UpdateAuditLogRetentionConfigurationDto { RetentionDays = 90, PrivateMessageRetentionDays = 0 }));
    }

    [Fact]
    public async Task Setting_AZeroRow_IsReadAsTheDefault()
    {
        // A zero would make the next run delete every message.
        var (service, _) = ConfigService(new AuditLogRetentionConfiguration { PrivateMessageRetentionDays = 0 });
        Assert.Equal(30, (await service.GetAsync()).PrivateMessageRetentionDays);
    }
}
