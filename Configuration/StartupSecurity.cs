using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.HttpOverrides;

namespace knkwebapi_v2.Configuration
{
    /// <summary>
    /// Startup checks and settings for running on the internet (closed-alpha hardening WP7, D9).
    /// </summary>
    public static class StartupSecurity
    {
        public const string JwtSecretKey = "Security:Jwt:Secret";
        public const string ConnectionStringName = "MySqlDbConnection";

        /// <summary>
        /// SHA-256 (hex) of every JWT secret that was ever committed to this repository
        /// (appsettings.json and appsettings.Development.json before WP7). Outside Development the
        /// API refuses to start with one of these. Only hashes are kept here, never the values.
        /// </summary>
        public static readonly IReadOnlySet<string> CommittedJwtSecretSha256 = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "2f696c5be9b3c0b8e2c3e6ed3d587691163eaf2a4299df3f98ddea87a7c7f626",
            "2502c62b2509e575ce104d84f86dcdd92eb991286b106669b290f431a60d09f1",
        };

        /// <summary>Why <paramref name="secret"/> can't sign tokens on the internet, or null when it can.</summary>
        public static string? JwtSecretProblem(string? secret)
        {
            if (string.IsNullOrWhiteSpace(secret))
            {
                return "Security:Jwt:Secret is not set";
            }
            if (secret.Length < 32)
            {
                return "Security:Jwt:Secret is shorter than 32 characters";
            }
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
            return CommittedJwtSecretSha256.Contains(hash)
                ? "Security:Jwt:Secret is a value that was committed to git; generate a new one (openssl rand -base64 48)"
                : null;
        }

        /// <summary>
        /// Fails fast outside Development on a missing or committed JWT secret or a missing connection
        /// string. In Development a missing JWT secret is generated for this run (sessions don't
        /// survive a restart; set a user-secret to keep them), and a missing connection string
        /// prints the user-secrets command. Returns warnings to log once the app is built.
        /// </summary>
        public static List<string> ApplySecretChecks(WebApplicationBuilder builder)
        {
            var warnings = new List<string>();
            var isDevelopment = builder.Environment.IsDevelopment();
            var configuration = builder.Configuration;

            if (string.IsNullOrWhiteSpace(configuration.GetConnectionString(ConnectionStringName)))
            {
                if (isDevelopment)
                {
                    Console.Error.WriteLine("Set the dev database: dotnet user-secrets set \"ConnectionStrings:MySqlDbConnection\" \"server=localhost;port=3306;database=knightsandkings_dev_v2;user=<user>;password=<password>\"");
                }
                throw new InvalidOperationException(
                    "ConnectionStrings:MySqlDbConnection is not set. Use dotnet user-secrets in Development or ConnectionStrings__MySqlDbConnection in the environment.");
            }

            var secretProblem = JwtSecretProblem(configuration[JwtSecretKey]);
            if (secretProblem != null)
            {
                if (!isDevelopment)
                {
                    throw new InvalidOperationException(
                        $"Refusing to start: {secretProblem}. Set Security__Jwt__Secret in the environment (openssl rand -base64 48).");
                }

                if (string.IsNullOrWhiteSpace(configuration[JwtSecretKey]))
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        [JwtSecretKey] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
                    });
                    warnings.Add("DEVELOPMENT: Security:Jwt:Secret is not set, so a random one was generated for this run. "
                        + "Every login ends when the API restarts. To keep sessions, run: dotnet user-secrets set \"Security:Jwt:Secret\" \"<openssl rand -base64 48>\"");
                }
                else
                {
                    warnings.Add($"DEVELOPMENT: {secretProblem}. The API would refuse to start like this outside Development.");
                }
            }

            return warnings;
        }

        /// <summary>
        /// ForwardedHeaders:Enabled (default false), :KnownProxies ("127.0.0.1;::1"),
        /// :KnownNetworks (CIDRs, e.g. the Docker network), :ForwardedForHeaderName
        /// (X-Forwarded-For, or CF-Connecting-IP behind Cloudflare). Returns whether it is enabled.
        /// </summary>
        public static bool ConfigureForwardedHeaders(IServiceCollection services, IConfiguration configuration)
        {
            var section = configuration.GetSection("ForwardedHeaders");
            if (!section.GetValue("Enabled", false))
            {
                return false;
            }

            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                var header = section["ForwardedForHeaderName"];
                if (!string.IsNullOrWhiteSpace(header))
                {
                    options.ForwardedForHeaderName = header;
                }
                options.KnownProxies.Clear();
                options.KnownNetworks.Clear();
                foreach (var proxy in SplitList(section["KnownProxies"] ?? "127.0.0.1;::1"))
                {
                    options.KnownProxies.Add(IPAddress.Parse(proxy));
                }
                foreach (var network in SplitList(section["KnownNetworks"]))
                {
                    var parts = network.Split('/');
                    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse(parts[0]), int.Parse(parts[1])));
                }
            });
            return true;
        }

        /// <summary>Cors:AllowedOrigins (array). Empty = no cross-origin callers (same-origin deploy).</summary>
        public static string[] AllowedOrigins(IConfiguration configuration) =>
            configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?.Where(o => !string.IsNullOrWhiteSpace(o)).ToArray()
            ?? Array.Empty<string>();

        private static IEnumerable<string> SplitList(string? value) =>
            (value ?? string.Empty).Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
