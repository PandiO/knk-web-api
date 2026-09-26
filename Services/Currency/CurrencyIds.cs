using System.Security.Cryptography;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// ULIDs for ledger public ids (currency DESIGN.md §3.2): 26 Crockford base32 characters,
    /// 48-bit millisecond timestamp then 80 random bits, so they sort by creation time and can be
    /// shown to players and staff ("TX 01J…") without exposing row counts.
    /// </summary>
    public static class CurrencyIds
    {
        private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        public static string NewPublicId() => NewPublicId(DateTimeOffset.UtcNow);

        public static string NewPublicId(DateTimeOffset at)
        {
            Span<byte> bytes = stackalloc byte[16];
            var ms = (ulong)at.ToUnixTimeMilliseconds();
            for (var i = 5; i >= 0; i--)
            {
                bytes[i] = (byte)(ms & 0xFF);
                ms >>= 8;
            }
            RandomNumberGenerator.Fill(bytes[6..]);

            // 128 bits → 26 base32 characters (the first carries only 3 bits).
            var high = 0UL;
            var low = 0UL;
            for (var i = 0; i < 8; i++) high = (high << 8) | bytes[i];
            for (var i = 8; i < 16; i++) low = (low << 8) | bytes[i];

            Span<char> chars = stackalloc char[26];
            for (var i = 25; i >= 0; i--)
            {
                chars[i] = Alphabet[(int)(low & 0x1F)];
                low = (low >> 5) | ((high & 0x1F) << 59);
                high >>= 5;
            }
            return new string(chars);
        }
    }
}
