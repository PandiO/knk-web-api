using System;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>A player's statistics visibility settings (KNG-34, DESIGN.md §F.4).</summary>
    public interface IStatisticsVisibilityService
    {
        /// <summary>Every setting with its metric-level value and its contexts (overrides and
        /// contexts with data). Null when the user doesn't exist.</summary>
        Task<StatisticsVisibilityDto?> GetAsync(int userId, CancellationToken ct = default);

        /// <summary>
        /// Applies all changes atomically: each change's <c>expected</c> must equal the value that
        /// applies now (for a context without an override: the inherited metric-level value),
        /// otherwise nothing is written and <see cref="StatisticsVisibilityConflictException"/> is
        /// thrown. Validation failures throw <see cref="StatisticsValidationException"/>. Null when
        /// the user doesn't exist.
        /// </summary>
        Task<StatisticsVisibilityDto?> UpdateAsync(int userId, StatisticsVisibilityUpdateDto update, CancellationToken ct = default);
    }

    /// <summary>A 400 with a stable error code (UnknownSetting, NotContextual, InvalidContext, …).</summary>
    public class StatisticsValidationException : ArgumentException
    {
        public StatisticsValidationException(string code, string message) : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }

    /// <summary>409 VisibilityConflict: an expected value didn't match; carries the current settings.</summary>
    public class StatisticsVisibilityConflictException : Exception
    {
        public StatisticsVisibilityConflictException(StatisticsVisibilityDto current)
            : base("Your statistics settings changed in the meantime; nothing was changed. Review the current settings and try again.")
        {
            Current = current;
        }

        public StatisticsVisibilityDto Current { get; }
    }

    /// <summary>403: the viewer may not see this statistic.</summary>
    public class StatisticsHiddenException : Exception
    {
        public StatisticsHiddenException(string message) : base(message)
        {
        }
    }
}
