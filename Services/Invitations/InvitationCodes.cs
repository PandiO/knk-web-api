using System;
using System.Security.Cryptography;
using System.Text;

namespace knkwebapi_v2.Services.Invitations
{
    /// <summary>
    /// Invitation code formats (docs/specs/invitations/DESIGN.md § 3.3, KNG-212). Personal links carry
    /// 32 random bytes as base64url (43 characters); shared codes are KNK- plus 10 Crockford base32
    /// characters, shown as KNK-XXXXX-XXXXX and read case-insensitively with hyphens, spaces and the
    /// look-alikes O/I/L ignored. Only <see cref="Hash"/> of the normalised code is stored.
    /// </summary>
    public static class InvitationCodes
    {
        /// <summary>Crockford base32: no I, L, O or U.</summary>
        public const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        public const string SharedPrefix = "KNK";

        public const int SharedLength = 10;

        public static string NewPersonalToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>A new shared code in its display form, KNK-XXXXX-XXXXX.</summary>
        public static string NewSharedCode() => FormatShared(RandomCrockford(SharedLength));

        /// <summary>The internal web-first username: web- plus 10 Crockford characters. The hyphen can't
        /// occur in a Minecraft name, so nobody can join with it (DESIGN.md § 5.4).</summary>
        public static string NewShellUsername() => "web-" + RandomCrockford(10).ToLowerInvariant();

        public static string RandomCrockford(int length)
        {
            var chars = new char[length];
            for (var i = 0; i < length; i++)
            {
                chars[i] = Crockford[RandomNumberGenerator.GetInt32(Crockford.Length)];
            }
            return new string(chars);
        }

        public static string FormatShared(string body) =>
            $"{SharedPrefix}-{body[..5]}-{body[5..]}";

        /// <summary>
        /// The 10 body characters of a typed shared code (with or without KNK-, any case, hyphens or
        /// spaces, O read as 0 and I/L as 1), or null when it can't be one.
        /// </summary>
        public static string? NormalizeShared(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }
            var builder = new StringBuilder(raw.Length);
            foreach (var c in raw.Trim().ToUpperInvariant())
            {
                if (c == '-' || char.IsWhiteSpace(c))
                {
                    continue;
                }
                builder.Append(c);
            }
            var compact = builder.ToString();
            if (compact.StartsWith(SharedPrefix, StringComparison.Ordinal) && compact.Length == SharedPrefix.Length + SharedLength)
            {
                compact = compact[SharedPrefix.Length..];
            }
            if (compact.Length != SharedLength)
            {
                return null;
            }
            var body = new char[SharedLength];
            for (var i = 0; i < SharedLength; i++)
            {
                var c = compact[i] switch { 'O' => '0', 'I' or 'L' => '1', var other => other };
                if (Crockford.IndexOf(c) < 0)
                {
                    return null;
                }
                body[i] = c;
            }
            return new string(body);
        }

        /// <summary>A personal token as sent (base64url, 43 characters), or null.</summary>
        public static string? NormalizePersonal(string? raw)
        {
            var token = raw?.Trim();
            if (string.IsNullOrEmpty(token) || token.Length != 43)
            {
                return null;
            }
            foreach (var c in token)
            {
                if (!(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_'))
                {
                    return null;
                }
            }
            return token;
        }

        /// <summary>Lower-case hex SHA-256 of an already normalised code.</summary>
        public static string Hash(string normalized) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();

        /// <summary>The last 4 characters, shown in the staff list.</summary>
        public static string Hint(string normalized) => normalized[^4..];
    }
}
