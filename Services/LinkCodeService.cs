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
        private const string ValidChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        private const int CodeLength = 8;

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
        public Task<string> GenerateCodeAsync()
        {
            var code = new char[CodeLength];
            var randomBytes = new byte[CodeLength];

            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }

            for (int i = 0; i < CodeLength; i++)
            {
                code[i] = ValidChars[randomBytes[i] % ValidChars.Length];
            }

            return Task.FromResult(new string(code));
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

            if (normalizedCode.Length != CodeLength)
            {
                return (false, null, "Invalid link code format.");
            }

            var linkCode = await _linkCodeRepository.GetLinkCodeByCodeAsync(normalizedCode);

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
    }
}
