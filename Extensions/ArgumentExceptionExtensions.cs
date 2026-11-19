using System;

namespace knkwebapi_v2.Extensions
{
    public static class ArgumentExceptionExtensions
    {
        /// <summary>
        /// The message without .NET's " (Parameter 'dto')" suffix, for <c>{ error }</c> bodies the web app shows as they
        /// are (PandiO/knk-workspace#831). <see cref="ArgumentException.Message"/> adds the suffix whenever a parameter
        /// name was given.
        /// </summary>
        public static string PlainMessage(this ArgumentException ex)
        {
            var message = ex.Message;
            if (string.IsNullOrEmpty(ex.ParamName)) return message;
            var suffix = $" (Parameter '{ex.ParamName}')";
            return message.EndsWith(suffix, StringComparison.Ordinal) ? message[..^suffix.Length] : message;
        }
    }
}
