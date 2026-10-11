using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Configuration;

/// <summary>
/// The MySQL server version handed to <c>UseMySql</c>, resolved once per process instead of once per
/// DbContext. The AddDbContext options lambda runs for every DI scope (each request, each background
/// iteration); calling <c>ServerVersion.AutoDetect</c> there opened an extra connection every time,
/// and with MySQL down it made DbContext construction itself throw (found in the KNG-115 live test).
/// <para>
/// <see cref="ConfigKey"/> set (e.g. "8.0.36", or "10.11.6-mariadb"): that version is used and the
/// DbContext never connects just to learn it. Unset: one <c>AutoDetect</c> on first use. A failed
/// detection is not cached, so a database that comes up after the API retries on the next DbContext.
/// </para>
/// </summary>
public sealed class MySqlServerVersionResolver
{
    public const string ConfigKey = "Database:MySqlServerVersion";

    private readonly Lazy<ServerVersion> _version;

    public MySqlServerVersionResolver(IConfiguration configuration, string? connectionString)
        : this(configuration[ConfigKey], () => ServerVersion.AutoDetect(connectionString))
    {
    }

    /// <param name="configuredVersion">The <see cref="ConfigKey"/> value; blank means detect.</param>
    /// <param name="detect">Runs at most until it first succeeds.</param>
    public MySqlServerVersionResolver(string? configuredVersion, Func<ServerVersion> detect)
    {
        if (string.IsNullOrWhiteSpace(configuredVersion))
        {
            // PublicationOnly: unlike the default mode, a throwing factory is not cached.
            _version = new Lazy<ServerVersion>(detect, LazyThreadSafetyMode.PublicationOnly);
            return;
        }

        if (!ServerVersion.TryParse(configuredVersion.Trim(), out var parsed))
        {
            throw new InvalidOperationException(
                $"{ConfigKey} \"{configuredVersion}\" is not a server version such as \"8.0.36\" or \"10.11.6-mariadb\".");
        }
        _version = new Lazy<ServerVersion>(parsed);
        IsConfigured = true;
    }

    /// <summary>True when the version came from <see cref="ConfigKey"/> rather than detection.</summary>
    public bool IsConfigured { get; }

    public ServerVersion Version => _version.Value;
}
