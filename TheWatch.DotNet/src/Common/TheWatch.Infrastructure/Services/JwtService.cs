using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using TheWatch.Core.Interfaces;

namespace TheWatch.Infrastructure.Services;

/// <summary>
/// Implementation of JWT token generation and validation using RS256.
/// Uses RSA keypair for signing/verification as per security requirements.
/// </summary>
public class JwtService : IJwtService
{
    private readonly RSA _rsaPrivateKey;
    private readonly RSA _rsaPublicKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly RsaSecurityKey _signingKey;
    private readonly RsaSecurityKey _validationKey;
    private readonly JwtSecurityTokenHandler _tokenHandler;

    public JwtService(string rsaPrivateKeyPem, string rsaPublicKeyPem, string issuer, string audience)
    {
        if (string.IsNullOrWhiteSpace(rsaPrivateKeyPem))
        {
            throw new ArgumentException("RSA private key must be provided", nameof(rsaPrivateKeyPem));
        }

        if (string.IsNullOrWhiteSpace(rsaPublicKeyPem))
        {
            throw new ArgumentException("RSA public key must be provided", nameof(rsaPublicKeyPem));
        }

        _issuer = issuer ?? throw new ArgumentNullException(nameof(issuer));
        _audience = audience ?? throw new ArgumentNullException(nameof(audience));

        // Import RSA keys
        _rsaPrivateKey = RSA.Create();
        _rsaPrivateKey.ImportFromPem(rsaPrivateKeyPem);

        _rsaPublicKey = RSA.Create();
        _rsaPublicKey.ImportFromPem(rsaPublicKeyPem);

        _signingKey = new RsaSecurityKey(_rsaPrivateKey) { KeyId = "the-watch-key-1" };
        _validationKey = new RsaSecurityKey(_rsaPublicKey) { KeyId = "the-watch-key-1" };

        _tokenHandler = new JwtSecurityTokenHandler();
    }

    public string GenerateAccessToken(Guid userId, string[] roles, int expirationMinutes = 15)
    {
        if (roles == null || roles.Length == 0)
        {
            throw new ArgumentException("At least one role must be provided", nameof(roles));
        }

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        // Add all roles as separate claims
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _issuer,
            Audience = _audience,
            Expires = DateTime.UtcNow.AddMinutes(expirationMinutes),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256)
        };

        var token = _tokenHandler.CreateToken(tokenDescriptor);
        return _tokenHandler.WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        // Generate cryptographically secure random refresh token
        byte[] randomBytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(randomBytes);
    }

    public async Task<Guid?> ValidateAccessTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _validationKey,
                ClockSkew = TimeSpan.FromMinutes(1) // Allow 1 minute clock skew
            };

            var principal = await _tokenHandler.ValidateTokenAsync(token, validationParameters);

            if (!principal.IsValid)
            {
                return null;
            }

            var userIdClaim = principal.ClaimsIdentity?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out Guid userId))
            {
                return null;
            }

            return userId;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public Task<string> GetJwksJsonAsync()
    {
        // Create JWKS (JSON Web Key Set) for token validation by external clients
        var parameters = _rsaPublicKey.ExportParameters(false);

        var jwk = new
        {
            kty = "RSA",
            use = "sig",
            kid = "the-watch-key-1",
            alg = "RS256",
            n = Convert.ToBase64String(parameters.Modulus!),
            e = Convert.ToBase64String(parameters.Exponent!)
        };

        var jwks = new
        {
            keys = new[] { jwk }
        };

        string json = JsonSerializer.Serialize(jwks, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        return Task.FromResult(json);
    }

    public string GenerateStepUpToken(Guid userId, string purpose, int expirationMinutes = 5)
    {
        if (string.IsNullOrWhiteSpace(purpose))
        {
            throw new ArgumentException("Purpose must be provided for step-up token", nameof(purpose));
        }

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("step_up", "true"),
            new Claim("purpose", purpose),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _issuer,
            Audience = _audience,
            Expires = DateTime.UtcNow.AddMinutes(expirationMinutes),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256)
        };

        var token = _tokenHandler.CreateToken(tokenDescriptor);
        return _tokenHandler.WriteToken(token);
    }
}
