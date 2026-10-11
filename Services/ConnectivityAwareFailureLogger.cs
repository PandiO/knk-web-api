using System.IO;
using System.Net.Sockets;
using MySqlConnector;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Failure logging for a background loop that retries every few seconds or minutes. While MySQL
    /// is down every iteration fails the same way (usually while the DbContext is built), so a
    /// database-connectivity failure is logged once, short and without a stack trace, when it
    /// starts; nothing while it continues; and one Information line when an iteration succeeds
    /// again. Any other failure keeps the full <c>LogError(ex, ...)</c> every time. Same
    /// once-per-change idea as the /health/ready logging in HealthCheckController (KNG-115).
    /// <para>
    /// One instance per loop (or per independently failing step): it holds that loop's state and
    /// is not thread-safe, which the sequential PeriodicTimer loops don't need.
    /// </para>
    /// </summary>
    public sealed class ConnectivityAwareFailureLogger
    {
        // MySqlConnector's codes for "the server can't be reached or dropped us". Other MySQL
        // errors (duplicate key, unknown column, deadlock...) are bugs or data problems and keep
        // their stack trace on every iteration.
        private static readonly HashSet<MySqlErrorCode> ConnectivityErrorCodes = new()
        {
            MySqlErrorCode.UnableToConnectToHost,
            MySqlErrorCode.ConnectionCountError,
            MySqlErrorCode.TooManyUserConnections,
            MySqlErrorCode.HostIsBlocked,
            MySqlErrorCode.AbortingConnection,
            MySqlErrorCode.NewAbortingConnection,
            MySqlErrorCode.NormalShutdown,
            MySqlErrorCode.ServerShutdown,
            MySqlErrorCode.ShutdownComplete,
        };

        private readonly ILogger _logger;
        private readonly string _operation;

        public ConnectivityAwareFailureLogger(ILogger logger, string operation)
        {
            _logger = logger;
            _operation = operation;
        }

        /// <summary>True between a logged connectivity failure and the next success.</summary>
        public bool DatabaseUnreachable { get; private set; }

        /// <summary>
        /// Logs a failed iteration: once per outage for a connectivity error, otherwise
        /// <c>LogError(ex, message, args)</c> as before.
        /// </summary>
        public void Failed(Exception ex, string message, params object?[] args)
        {
            if (!IsDatabaseConnectivityError(ex))
            {
                _logger.LogError(ex, message, args);
                return;
            }
            if (DatabaseUnreachable)
            {
                return;
            }
            DatabaseUnreachable = true;
            _logger.LogWarning("{Operation}: database unreachable ({Error}); retrying quietly until it is back",
                _operation, (FindMySqlException(ex) ?? ex).Message);
        }

        /// <summary>Call after a successful iteration; logs once if the database was unreachable.</summary>
        public void Succeeded()
        {
            if (!DatabaseUnreachable)
            {
                return;
            }
            DatabaseUnreachable = false;
            _logger.LogInformation("{Operation}: database reachable again", _operation);
        }

        /// <summary>
        /// A <see cref="MySqlException"/> anywhere in the InnerException chain (EF Core wraps it in
        /// DbUpdateException, InvalidOperationException...) that means the server can't be reached:
        /// a connectivity error code, or a socket/stream failure underneath.
        /// </summary>
        public static bool IsDatabaseConnectivityError(Exception ex)
        {
            var mySql = FindMySqlException(ex);
            return mySql != null
                && (ConnectivityErrorCodes.Contains(mySql.ErrorCode)
                    || mySql.InnerException is SocketException or IOException);
        }

        private static MySqlException? FindMySqlException(Exception ex)
        {
            for (Exception? current = ex; current != null; current = current.InnerException)
            {
                if (current is MySqlException mySql)
                {
                    return mySql;
                }
            }
            return null;
        }
    }
}
