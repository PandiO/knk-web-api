using System.Security.Cryptography;
using AutoMapper;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Service for generating and managing link codes for account linking.
    /// Uses cryptographically secure random generation (RandomNumberGenerator).
    /// </summary>
    public class LinkCodeService : ILinkCodeService
    {
        private readonly ILinkCodeRepository _linkCodeRepository;
        private readonly IUserRepository _userRepository;
        private readonly IMapper _mapper;
        private readonly SecuritySettings _securitySettings;
        /// <summary>Link codes: A-Z0-9 (36^8). Typed into a web form, so the full alphabet is fine.</summary>
        public const string ValidChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        /// <summary>
        /// Claim codes: Crockford base32 (32^8), read off a disconnect screen and typed by hand, so
        /// the look-alikes I, L, O and U are left out and <see cref="NormalizeClaimCode"/> maps the
        /// first three back (O→0, I/L→1).
        /// </summary>
        public const string ClaimChars = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        public const int CodeLength = 8;

        public LinkCodeService(
            ILinkCodeRepository linkCodeRepository,
            IUserRepository userRepository,
            IMapper mapper,
            IOptions<SecuritySettings> securitySettings)
        {
            _linkCodeRepository = linkCodeRepository;
            _userRepository = userRepository;
            _mapper = mapper;
            _securitySettings = securitySettings.Value;
        }

        /// <inheritdoc/>
        public Task<string> GenerateCodeAsync() => Task.FromResult(GenerateCode(ValidChars));

        /// <summary>
        /// A code of <see cref="CodeLength"/> characters drawn from <paramref name="alphabet"/>
        /// with <see cref="RandomNumberGenerator.GetInt32(int)"/>, which rejects out-of-range
        /// draws instead of folding a byte with modulo (KNG-208: byte % 36 favoured A-D by 8/7).
        /// </summary>
        public static string GenerateCode(string alphabet)
        {
            var code = new char[CodeLength];
            for (int i = 0; i < CodeLength; i++)
            {
                code[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            }

            return new string(code);
        }

        /// <summary>
        /// What a player typed from the disconnect screen, as stored: upper case, without hyphens
        /// or spaces, with the Crockford look-alikes folded (O→0, I→1, L→1). Never applied to link
        /// codes, whose alphabet has those letters.
        /// </summary>
        public static string NormalizeClaimCode(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var result = new System.Text.StringBuilder(input.Length);
            foreach (var raw in input.Trim().ToUpperInvariant())
            {
                var c = raw switch
                {
                    'O' => '0',
                    'I' or 'L' => '1',
                    _ => raw
                };
                if (c == '-' || c == ' ')
                {
                    continue;
                }
                result.Append(c);
            }
            return result.ToString();
        }

        /// <summary>XXXX-XXXX, the way the claim code is shown on the disconnect screen.</summary>
        public static string FormatClaimCode(string code) =>
            code.Length == CodeLength ? $"{code[..4]}-{code[4..]}" : code;

        /// <inheritdoc/>
        public async Task<LinkCode> CreateClaimCodeAsync(string minecraftUuid, string minecraftName, TimeSpan timeToLive)
        {
            if (string.IsNullOrWhiteSpace(minecraftUuid))
            {
                throw new ArgumentException("A Minecraft UUID is required.", nameof(minecraftUuid));
            }
            if (timeToLive <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeToLive));
            }

            var uuid = minecraftUuid.Trim().ToLowerInvariant();
            var existing = await _linkCodeRepository.GetActiveClaimCodeByUuidAsync(uuid);
            if (existing != null)
            {
                return existing;
            }

            string code;
            int attempts = 0;
            do
            {
                code = GenerateCode(ClaimChars);
                if (++attempts > 10)
                {
                    throw new InvalidOperationException("Failed to generate a unique claim code after multiple attempts");
                }
            } while (await _linkCodeRepository.GetLinkCodeByCodeAsync(code) != null);

            var now = DateTime.UtcNow;
            return await _linkCodeRepository.CreateAsync(new LinkCode
            {
                UserId = null,
                Code = code,
                Purpose = LinkCodePurpose.Claim,
                MinecraftUuid = uuid,
                MinecraftName = string.IsNullOrWhiteSpace(minecraftName) ? null : minecraftName.Trim(),
                CreatedAt = now,
                ExpiresAt = now.Add(timeToLive),
                Status = LinkCodeStatus.Active
            });
        }

        /// <inheritdoc/>
        public async Task<LinkCodeResponseDto> GenerateLinkCodeAsync(int? userId)
        {
            // Generate unique code (check for collision)
            string code;
            LinkCode? existing;
            int attempts = 0;
            const int maxAttempts = 10;

            do
            {
                code = await GenerateCodeAsync();
                existing = await _linkCodeRepository.GetLinkCodeByCodeAsync(code);
                attempts++;

                if (attempts >= maxAttempts)
                {
                    throw new InvalidOperationException("Failed to generate unique link code after multiple attempts");
                }
            } while (existing != null);

            var linkCode = new LinkCode
            {
                UserId = userId,
                Code = code,
                Purpose = LinkCodePurpose.Link,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(_securitySettings.LinkCodeExpirationMinutes),
                Status = LinkCodeStatus.Active
            };

            var created = await _linkCodeRepository.CreateAsync(linkCode);
            return _mapper.Map<LinkCodeResponseDto>(created);
        }

        /// <inheritdoc/>
        public async Task<(bool IsValid, LinkCode? LinkCode, string? Error)> ValidateLinkCodeAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return (false, null, "Link code is required.");
            }

            // Normalize code (remove hyphens, uppercase)
            var normalizedCode = code.Replace("-", "").ToUpperInvariant();

            // A claim code typed off the disconnect screen may hold Crockford look-alikes (O, I, L)
            // or spaces; only a claim code may match in that folded form (KNG-214).
            var claimForm = NormalizeClaimCode(code);
            if (normalizedCode.Length != CodeLength && claimForm.Length != CodeLength)
            {
                return (false, null, "Invalid link code format.");
            }

            var linkCode = normalizedCode.Length == CodeLength
                ? await _linkCodeRepository.GetLinkCodeByCodeAsync(normalizedCode)
                : null;
            if (linkCode == null && claimForm.Length == CodeLength && claimForm != normalizedCode)
            {
                var claim = await _linkCodeRepository.GetLinkCodeByCodeAsync(claimForm);
                linkCode = claim?.Purpose == LinkCodePurpose.Claim ? claim : null;
            }

            if (linkCode == null)
            {
                return (false, null, "Link code not found.");
            }

            if (linkCode.Status == LinkCodeStatus.Used)
            {
                return (false, linkCode, "Link code has already been used.");
            }

            if (linkCode.Status == LinkCodeStatus.Expired || linkCode.ExpiresAt < DateTime.UtcNow)
            {
                // Auto-update status to Expired if not already
                if (linkCode.Status != LinkCodeStatus.Expired)
                {
                    await _linkCodeRepository.UpdateLinkCodeStatusAsync(linkCode.Id, LinkCodeStatus.Expired);
                }
                return (false, linkCode, "Link code has expired.");
            }

            return (true, linkCode, null);
        }

        /// <inheritdoc/>
        public async Task<(bool Success, LinkCode? LinkCode, string? Error)> ConsumeLinkCodeAsync(string code)
        {
            var (isValid, linkCode, error) = await ValidateLinkCodeAsync(code);

            if (!isValid || linkCode == null)
            {
                return (false, linkCode, error);
            }

            // Mark as used with a conditional write: when two requests present the same code at
            // once, only the one whose write flips Active -> Used gets to continue.
            if (!await _linkCodeRepository.TryMarkUsedAsync(linkCode.Id))
            {
                return (false, linkCode, "This link code has already been used.");
            }
            linkCode.Status = LinkCodeStatus.Used;
            linkCode.UsedAt = DateTime.UtcNow;

            return (true, linkCode, null);
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<LinkCode>> GetExpiredCodesAsync()
        {
            return await _linkCodeRepository.GetExpiredLinkCodesAsync();
        }

        /// <inheritdoc/>
        public async Task<int> CleanupExpiredCodesAsync()
        {
            var expiredCodes = await _linkCodeRepository.GetExpiredLinkCodesAsync();
            var count = 0;

            foreach (var linkCode in expiredCodes)
            {
                // Update status to Expired if still Active
                if (linkCode.Status == LinkCodeStatus.Active)
                {
                    await _linkCodeRepository.UpdateLinkCodeStatusAsync(linkCode.Id, LinkCodeStatus.Expired);
                    count++;
                }
            }

            return count;
        }

        /// <inheritdoc/>
        public Task<int> DeleteFinishedCodesAsync(DateTime before) =>
            _linkCodeRepository.DeleteFinishedBeforeAsync(before);
    }
}
