using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TheWatch.Microservices.Security.AuthService.Models;

namespace TheWatch.Microservices.Security.AuthService.Services;

public interface IAuthService
{
    Task<(bool Success, string? Error, LoginResponse? Response)> RegisterAsync(RegisterRequest request);
    Task<(bool Success, string? Error, LoginResponse? Response)> LoginAsync(LoginRequest request);
    Task<TokenValidationResponse> ValidateTokenAsync(string token);
    Task<(bool Success, string? Error, LoginResponse? Response)> RefreshTokenAsync(string refreshToken);
    Task<IEnumerable<UserProfile>> GetAllUsersAsync();
}

public class AuthServiceImplementation : IAuthService
{
    private static readonly ConcurrentDictionary<string, User> UsersByUsername = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, User> UsersById = new();
    private static readonly ConcurrentDictionary<string, string> RefreshTokens = new(); // refreshToken -> userId

    private readonly string _jwtSecret = "TheWatchEnterpriseEmergencySecretKey2026!SecureAndDistributedMeshTokenAuth";
    private readonly string _issuer = "TheWatch.AuthService";
    private readonly string _audience = "TheWatch.Microservices";

    static AuthServiceImplementation()
    {
        // Seed default system users
        SeedUser("commander.dan", "dan@thewatch.gov", "Commander Dan Evans", "Password123!", UserRole.Commander, "HQ-ALPHA");
        SeedUser("dispatch.sarah", "sarah@thewatch.gov", "Sarah Chen", "Password123!", UserRole.Dispatcher, "DISPATCH-1");
        SeedUser("medic.alex", "alex@thewatch.gov", "Alex Rodriguez, EMT-P", "Password123!", UserRole.Paramedic, "MEDIC-42");
        SeedUser("drone.operator1", "drones@thewatch.gov", "Taylor Swift Flight Team", "Password123!", UserRole.DroneOperator, "DRONE-UNIT-9");
        SeedUser("admin", "admin@thewatch.gov", "System Administrator", "Admin2026!", UserRole.Admin, "ADMIN-0");
    }

    private static void SeedUser(string username, string email, string fullName, string password, UserRole role, string unitCallsign)
    {
        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var hash = HashPassword(password, salt);
        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = username,
            Email = email,
            FullName = fullName,
            Salt = salt,
            PasswordHash = hash,
            Role = role,
            UnitCallsign = unitCallsign,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        UsersByUsername[username] = user;
        UsersById[user.Id] = user;
    }

    private static string HashPassword(string password, string salt)
    {
        using var sha256 = SHA256.Create();
        var combined = Encoding.UTF8.GetBytes(password + salt);
        var hash = sha256.ComputeHash(combined);
        return Convert.ToBase64String(hash);
    }

    public Task<(bool Success, string? Error, LoginResponse? Response)> RegisterAsync(RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return Task.FromResult<(bool, string?, LoginResponse?)>((false, "Username and password are required.", null));

        if (UsersByUsername.ContainsKey(request.Username))
            return Task.FromResult<(bool, string?, LoginResponse?)>((false, "Username already exists.", null));

        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var hash = HashPassword(request.Password, salt);
        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = request.Username,
            Email = request.Email,
            FullName = request.FullName,
            Salt = salt,
            PasswordHash = hash,
            Role = request.Role,
            UnitCallsign = request.UnitCallsign,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        UsersByUsername[user.Username] = user;
        UsersById[user.Id] = user;

        var token = GenerateJwtToken(user);
        var refreshToken = Guid.NewGuid().ToString("N");
        RefreshTokens[refreshToken] = user.Id;

        var response = new LoginResponse
        {
            Token = token,
            RefreshToken = refreshToken,
            TokenType = "Bearer",
            ExpiresInSeconds = 86400,
            User = new UserProfile
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role.ToString(),
                UnitCallsign = user.UnitCallsign
            }
        };

        return Task.FromResult<(bool, string?, LoginResponse?)>((true, null, response));
    }

    public Task<(bool Success, string? Error, LoginResponse? Response)> LoginAsync(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return Task.FromResult<(bool, string?, LoginResponse?)>((false, "Invalid username or password.", null));

        if (!UsersByUsername.TryGetValue(request.Username, out var user))
            return Task.FromResult<(bool, string?, LoginResponse?)>((false, "Invalid credentials.", null));

        if (!user.IsActive)
            return Task.FromResult<(bool, string?, LoginResponse?)>((false, "Account is disabled.", null));

        var computedHash = HashPassword(request.Password, user.Salt);
        if (computedHash != user.PasswordHash)
            return Task.FromResult<(bool, string?, LoginResponse?)>((false, "Invalid credentials.", null));

        user.LastLoginUtc = DateTime.UtcNow;

        var token = GenerateJwtToken(user);
        var refreshToken = Guid.NewGuid().ToString("N");
        RefreshTokens[refreshToken] = user.Id;

        var response = new LoginResponse
        {
            Token = token,
            RefreshToken = refreshToken,
            TokenType = "Bearer",
            ExpiresInSeconds = 86400,
            User = new UserProfile
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role.ToString(),
                UnitCallsign = user.UnitCallsign
            }
        };

        return Task.FromResult<(bool, string?, LoginResponse?)>((true, null, response));
    }

    public Task<TokenValidationResponse> ValidateTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Task.FromResult(new TokenValidationResponse { IsValid = false, Error = "Empty token" });

        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_jwtSecret);

        try
        {
            var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            }, out var validatedToken);

            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var username = principal.FindFirst(ClaimTypes.Name)?.Value;
            var role = principal.FindFirst(ClaimTypes.Role)?.Value;

            return Task.FromResult(new TokenValidationResponse
            {
                IsValid = true,
                UserId = userId,
                Username = username,
                Role = role
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new TokenValidationResponse
            {
                IsValid = false,
                Error = ex.Message
            });
        }
    }

    public Task<(bool Success, string? Error, LoginResponse? Response)> RefreshTokenAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || !RefreshTokens.TryGetValue(refreshToken, out var userId))
            return Task.FromResult<(bool, string?, LoginResponse?)>((false, "Invalid refresh token.", null));

        if (!UsersById.TryGetValue(userId, out var user))
            return Task.FromResult<(bool, string?, LoginResponse?)>((false, "User not found.", null));

        // Revoke previous refresh token
        RefreshTokens.TryRemove(refreshToken, out _);

        var newJwt = GenerateJwtToken(user);
        var newRefreshToken = Guid.NewGuid().ToString("N");
        RefreshTokens[newRefreshToken] = user.Id;

        var response = new LoginResponse
        {
            Token = newJwt,
            RefreshToken = newRefreshToken,
            TokenType = "Bearer",
            ExpiresInSeconds = 86400,
            User = new UserProfile
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role.ToString(),
                UnitCallsign = user.UnitCallsign
            }
        };

        return Task.FromResult<(bool, string?, LoginResponse?)>((true, null, response));
    }

    public Task<IEnumerable<UserProfile>> GetAllUsersAsync()
    {
        var list = UsersById.Values.Select(u => new UserProfile
        {
            Id = u.Id,
            Username = u.Username,
            Email = u.Email,
            FullName = u.FullName,
            Role = u.Role.ToString(),
            UnitCallsign = u.UnitCallsign
        });
        return Task.FromResult(list);
    }

    private string GenerateJwtToken(User user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_jwtSecret);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim("unitCallsign", user.UnitCallsign)
            }),
            Expires = DateTime.UtcNow.AddDays(1),
            Issuer = _issuer,
            Audience = _audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}
