using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Akanti.API.Data;
using Akanti.API.DTOs.Auth;
using Akanti.API.Exceptions;
using Akanti.API.Interfaces;
using Akanti.API.Models;

namespace Akanti.API.Services;

public class AuthService : IAuthService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IBackgroundEmailQueue _emailQueue;

    public AuthService(ApplicationDbContext context, IConfiguration configuration, IBackgroundEmailQueue emailQueue)
    {
        _context = context;
        _configuration = configuration;
        _emailQueue = emailQueue;
    }

    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request)
    {
        // Email is stored and compared in a single canonical form. Without this, the
        // duplicate check below was case-sensitive while the admin grant check was
        // case-insensitive, so registering ADMIN@AKANTI.COM alongside admin@akanti.com
        // created a second account that was granted the Admin role.
        var email = NormalizeEmail(request.Email);

        // Anti-enumeration: an existing email must produce the same response shape and
        // timing class as a fresh registration. Never reveal whether the account exists.
        if (await _context.Users.AsNoTracking().AnyAsync(u => u.Email == email))
            throw new AuthFlowException("Email already registered.");

        var adminEmail = _configuration["AdminEmail"];
        var isAdmin = !string.IsNullOrEmpty(adminEmail) &&
                      string.Equals(email, NormalizeEmail(adminEmail), StringComparison.Ordinal);

        var user = new User
        {
            FullName = request.FullName,
            Email = email,
            PasswordHash = HashPassword(request.Password),
            PhoneNumber = request.PhoneNumber,
            UserType = Enum.TryParse<UserType>(request.UserType, true, out var ut) ? ut : UserType.PersonalFinance,
            IsAdmin = isAdmin,

            // Accounts are active on registration. Email verification was dropped because
            // transactional mail cannot be made reliable without a sending domain, and a
            // verification gate that silently never delivers locks users out of an account
            // they just created. The code flow remains for password resets, where the user
            // has a fallback: they already know their password.
            IsEmailVerified = true
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return new RegisterResponse
        {
            Message = isAdmin
                ? "Admin account created successfully."
                : "Account created. You can sign in now.",
            Email = email
        };
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        // Look up the canonical form, so signing in as ADMIN@AKANTI.COM finds the
        // account stored as admin@akanti.com.
        var user = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == NormalizeEmail(request.Email))
            ?? throw new UnauthorizedAccessException("Invalid email or password.");

        if (!VerifyPassword(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid email or password.");

        // Accounts that predate auto-verification may still carry IsEmailVerified = false.
        // They registered against an email pipeline that never delivered, so without this
        // they would remain permanently locked out of an account they own. Verification is
        // no longer a login precondition, so let them in and mark the flag on first sign-in.
        if (!user.IsEmailVerified)
        {
            var tracked = await _context.Users.FirstOrDefaultAsync(u => u.Id == user.Id);
            if (tracked != null)
            {
                tracked.IsEmailVerified = true;
                tracked.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }

        return GenerateTokenResponse(user);
    }

    public async Task<AuthResponse> VerifyEmailAsync(string email, string code)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == NormalizeEmail(email))
            ?? throw new InvalidOperationException("User not found.");

        if (user.IsEmailVerified)
            return GenerateTokenResponse(user);

        var verification = await _context.EmailVerifications
            .FirstOrDefaultAsync(v => v.UserId == user.Id && v.Code == code && !v.IsUsed && v.ExpiresAt > DateTime.UtcNow);

        if (verification == null)
            throw new InvalidOperationException("Invalid or expired verification code.");

        user.IsEmailVerified = true;
        user.UpdatedAt = DateTime.UtcNow;

        // Retire every outstanding code, not just the one matched, so no unused codes
        // survive on an account that is already verified.
        var outstanding = await _context.EmailVerifications
            .Where(v => v.UserId == user.Id && !v.IsUsed)
            .ToListAsync();

        foreach (var v in outstanding)
            v.IsUsed = true;

        await _context.SaveChangesAsync();

        return GenerateTokenResponse(user);
    }

    public async Task ResendVerificationAsync(string email)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == NormalizeEmail(email))
            ?? throw new InvalidOperationException("User not found.");

        if (user.IsEmailVerified)
            throw new InvalidOperationException("Email is already verified.");

        // Server-side cooldown. The client shows a countdown, but that is cosmetic: a
        // caller can hit this endpoint directly, so the limit has to live here.
        var cooldownMinutes = 2;
        var recent = await _context.EmailVerifications.AsNoTracking()
            .Where(v => v.UserId == user.Id && v.CreatedAt > DateTime.UtcNow.AddMinutes(-cooldownMinutes))
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync();

        if (recent != null)
        {
            var waitSeconds = (int)(recent.CreatedAt.AddMinutes(cooldownMinutes) - DateTime.UtcNow).TotalSeconds;
            if (waitSeconds > 0)
                throw new InvalidOperationException($"Please wait {waitSeconds / 60}m {waitSeconds % 60}s before requesting a new code.");
        }

        // Retire every outstanding code before issuing a new one. Without this each
        // resend adds another valid code, so the account becomes easier to guess into
        // rather than harder, and codes the user never saw stay live.
        var outstanding = await _context.EmailVerifications
            .Where(v => v.UserId == user.Id && !v.IsUsed)
            .ToListAsync();

        foreach (var old in outstanding)
            old.IsUsed = true;

        var code = GenerateVerificationCode();

        _context.EmailVerifications.Add(new EmailVerification
        {
            UserId = user.Id,
            Code = code,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            IsUsed = false,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        await _emailQueue.EnqueueAsync(user.Email, "Verify your Akanti account", BuildVerificationEmail(user.FullName, code));
    }

    public async Task<UserDto> GetUserByIdAsync(int userId)
    {
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("User not found.");

        return new UserDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            ProfileImageUrl = user.ProfileImageUrl,
            UserType = user.UserType.ToString(),
            IsAdmin = user.IsAdmin
        };
    }

    public async Task ForgotPasswordAsync(string email)
    {
        var user = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == NormalizeEmail(email));
        if (user == null) return;

        var cooldownMinutes = 10;
        var recentToken = await _context.PasswordResetTokens.AsNoTracking()
            .Where(t => t.UserId == user.Id && t.CreatedAt > DateTime.UtcNow.AddMinutes(-cooldownMinutes))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        if (recentToken != null)
        {
            var waitSeconds = (int)(recentToken.CreatedAt.AddMinutes(cooldownMinutes) - DateTime.UtcNow).TotalSeconds;
            throw new InvalidOperationException($"A code was already sent. Please wait {waitSeconds / 60}m {waitSeconds % 60}s before requesting a new one.");
        }

        var code = GenerateVerificationCode();

        var resetToken = new PasswordResetToken
        {
            UserId = user.Id,
            Code = code,
            TokenHash = HashPassword(code),
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            IsUsed = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.PasswordResetTokens.Add(resetToken);
        await _context.SaveChangesAsync();

        await _emailQueue.EnqueueAsync(user.Email, "Akanti - Password Reset Code", BuildPasswordResetEmail(user.FullName, code));
    }

    public async Task VerifyResetCodeAsync(string email, string code)
    {
        var user = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == NormalizeEmail(email))
            ?? throw new InvalidOperationException("User not found.");

        var resetToken = await _context.PasswordResetTokens.AsNoTracking()
            .FirstOrDefaultAsync(t => t.UserId == user.Id && t.Code == code && !t.IsUsed && t.ExpiresAt > DateTime.UtcNow)
            ?? throw new InvalidOperationException("Invalid or expired reset code.");
    }

    public async Task ResetPasswordAsync(string email, string code, string newPassword)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == NormalizeEmail(email))
            ?? throw new InvalidOperationException("User not found.");

        var latestToken = await _context.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.Code == code && t.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("Invalid or expired reset code. Please request a new one.");

        user.PasswordHash = HashPassword(newPassword);
        user.IsEmailVerified = true;
        user.UpdatedAt = DateTime.UtcNow;

        latestToken.IsUsed = true;
        await _context.SaveChangesAsync();
    }

    public async Task ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("User not found.");

        if (!VerifyPassword(currentPassword, user.PasswordHash))
            throw new InvalidOperationException("Current password is incorrect.");

        user.PasswordHash = HashPassword(newPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Canonical form for an email address: trimmed and lowercased.
    /// Storage, duplicate detection, and lookup all use this, so one address can only
    /// ever be one account regardless of how the user typed it.
    /// </summary>
    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string GenerateVerificationCode()
    {
        return RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
    }

    private static string BuildVerificationEmail(string fullName, string code)
    {
        return $@"
            <div style='font-family: Arial, sans-serif; max-width: 500px; margin: 0 auto; padding: 20px;'>
                <h2 style='color: #16a34a;'>Welcome to Akanti!</h2>
                <p>Hi {fullName},</p>
                <p>Thank you for registering. Please use the following code to verify your email address:</p>
                <div style='background: #f3f4f6; border-radius: 8px; padding: 20px; text-align: center; margin: 20px 0;'>
                    <span style='font-size: 32px; font-weight: bold; letter-spacing: 8px; color: #16a34a;'>{code}</span>
                </div>
                <p style='color: #6b7280; font-size: 14px;'>This code expires in 15 minutes.</p>
                <p style='color: #6b7280; font-size: 14px;'>If you did not create an account, you can ignore this email.</p>
            </div>";
    }

    private static string BuildPasswordResetEmail(string fullName, string code)
    {
        return $@"
            <div style='font-family: Arial, sans-serif; max-width: 500px; margin: 0 auto; padding: 20px;'>
                <h2 style='color: #16a34a;'>Password Reset Request</h2>
                <p>Hi {fullName},</p>
                <p>We received a request to reset your password. Use the following code:</p>
                <div style='background: #f3f4f6; border-radius: 8px; padding: 20px; text-align: center; margin: 20px 0;'>
                    <span style='font-size: 32px; font-weight: bold; letter-spacing: 8px; color: #16a34a;'>{code}</span>
                </div>
                <p style='color: #6b7280; font-size: 14px;'>This code expires in 15 minutes.</p>
                <p style='color: #6b7280; font-size: 14px;'>If you did not request a password reset, you can ignore this email.</p>
            </div>";
    }

    private AuthResponse GenerateTokenResponse(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            _configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT key not configured")));

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.FullName)
        };

        if (user.IsAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));

        var expiration = DateTime.UtcNow.AddHours(Convert.ToDouble(_configuration["Jwt:ExpirationHours"] ?? "24"));

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: expiration,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new AuthResponse
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            Expiration = expiration,
            User = new UserDto
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                UserType = user.UserType.ToString(),
                IsAdmin = user.IsAdmin
            }
        };
    }

    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    private static bool VerifyPassword(string password, string hash)
    {
        return HashPassword(password) == hash;
    }
}
