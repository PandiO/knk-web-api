using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using knkwebapi_v2.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Email
{
    public sealed record RenderedEmail(string Subject, string TextBody, string HtmlBody);

    public sealed class EmailTemplateException : Exception
    {
        public EmailTemplateException(string message) : base(message)
        {
        }
    }

    public interface IEmailTemplateRenderer
    {
        RenderedEmail Render(EmailKind kind, object model);
    }

    /// <summary>
    /// Renders a mail from the embedded templates (Services/Email/Templates): the kind's .txt and
    /// .html pair inside _layout.txt / _layout.html. A template holds {{name}} placeholders filled
    /// from the model's properties (or dictionary keys), case-insensitive; every value is
    /// HTML-encoded in the .html part. Dates print as "yyyy-MM-dd HH:mm UTC". An unknown
    /// placeholder throws when <c>strict</c> (tests, Development) and renders empty otherwise.
    /// </summary>
    public sealed class EmailTemplateRenderer : IEmailTemplateRenderer
    {
        public const string ResourcePrefix = "EmailTemplates/";

        private static readonly Regex Placeholder = new(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}", RegexOptions.Compiled);
        private static readonly ConcurrentDictionary<string, string> Cache = new();

        private readonly EmailSettings _email;
        private readonly string _baseUrl;
        private readonly bool _strict;
        private readonly ILogger _logger;

        public EmailTemplateRenderer(IOptions<EmailSettings> email, IOptions<AppSettings>? app = null,
            IOptions<SecuritySettings>? security = null, IWebHostEnvironment? environment = null,
            ILogger<EmailTemplateRenderer>? logger = null)
            : this(email.Value, AppSettings.ResolveBaseUrl(app?.Value.PublicBaseUrl, security?.Value.PasswordResetFrontendBaseUrl),
                environment?.IsDevelopment() ?? false, logger)
        {
        }

        public EmailTemplateRenderer(EmailSettings email, string baseUrl, bool strict, ILogger? logger = null)
        {
            _email = email;
            _baseUrl = baseUrl.TrimEnd('/');
            _strict = strict;
            _logger = logger ?? NullLogger.Instance;
        }

        public RenderedEmail Render(EmailKind kind, object model)
        {
            var info = EmailKinds.Info(kind);
            var values = ToValues(model);

            var subject = Fill(info.Subject, values, htmlEncode: false, $"{kind} subject");
            var text = Fill(Load(info.Template + ".txt"), values, htmlEncode: false, $"{info.Template}.txt");
            var html = Fill(Load(info.Template + ".html"), values, htmlEncode: true, $"{info.Template}.html");

            var support = string.IsNullOrWhiteSpace(_email.ReplyToAddress)
                ? "Questions? Ask a staff member on the server."
                : $"Questions? Reply to this mail or write to {_email.ReplyToAddress.Trim()}.";
            var layout = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["subject"] = subject,
                ["reason"] = info.Reason,
                ["support"] = support,
                ["baseUrl"] = _baseUrl,
                ["logoUrl"] = _baseUrl + "/brand/knk-shield.png",
                ["year"] = DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture),
            };

            // The layout is filled first and the rendered part dropped in afterwards, so it goes in
            // raw (it is already encoded) and a value such as a username is never parsed twice.
            layout["content"] = ContentMarker;
            var textBody = Fill(Load("_layout.txt"), layout, htmlEncode: false, "_layout.txt").Replace(ContentMarker, text.Trim());
            var htmlBody = Fill(Load("_layout.html"), layout, htmlEncode: false, "_layout.html", encodeExcept: ContentMarker)
                .Replace(ContentMarker, html.Trim());

            return new RenderedEmail(subject, textBody, htmlBody);
        }

        private const string ContentMarker = "\u0001content\u0001";

        public string Fill(string template, IReadOnlyDictionary<string, string> values, bool htmlEncode, string source, string? encodeExcept = null)
        {
            return Placeholder.Replace(template, match =>
            {
                var name = match.Groups[1].Value;
                if (!values.TryGetValue(name, out var value))
                {
                    if (_strict)
                    {
                        throw new EmailTemplateException($"Unknown placeholder {{{{{name}}}}} in {source}.");
                    }
                    _logger.LogWarning("Unknown placeholder {Placeholder} in mail template {Source}; rendered empty", name, source);
                    return string.Empty;
                }
                var encode = htmlEncode || (encodeExcept != null && value != encodeExcept);
                return encode ? WebUtility.HtmlEncode(value) : value;
            });
        }

        /// <summary>The model's public properties (or an IDictionary's entries) as strings.</summary>
        public static IReadOnlyDictionary<string, string> ToValues(object model)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (model is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    values[entry.Key.ToString()!] = Format(entry.Value);
                }
                return values;
            }
            foreach (var property in model.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length == 0)
                {
                    values[property.Name] = Format(property.GetValue(model));
                }
            }
            return values;
        }

        private static string Format(object? value) => value switch
        {
            null => string.Empty,
            DateTime date => date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC",
            DateTimeOffset date => date.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        /// <summary>An embedded template by file name, e.g. "password-reset.html".</summary>
        public static string Load(string fileName) => Cache.GetOrAdd(fileName, name =>
        {
            var assembly = typeof(EmailTemplateRenderer).Assembly;
            using var stream = assembly.GetManifestResourceStream(ResourcePrefix + name)
                ?? throw new EmailTemplateException($"Mail template {name} is not embedded in the assembly.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Replace("\r\n", "\n");
        });
    }
}
