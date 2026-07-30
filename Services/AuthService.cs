using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Akanti.API.Data;
using Akanti.API.DTOs.Auth;
using Akanti.API.Models;

namespace Akanti.API.Services;

public class AuthService : IAuthService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IEmailService _emailService;

    public AuthService(ApplicationDbContext context, IConfiguration configuration, IEmailService emailService)
    {
        _context = context;
        _configuration = configuration;
        _emailService = emailService;
    }

    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request)
    {
        if (await _context.Users.AnyAsync(u => u.Email == request.Email))
            throw new InvalidOperationException("Email Address already exists.");

        var adminEmail = _configuration["AdminEmail"];
        var isAdmin = !string.IsNullOrEmpty(adminEmail) &&
                      string.Equals(request.Email, adminEmail, StringComparison.OrdinalIgnoreCase);

        var user = new User
        {
            FullName = request.FullName,
            Email = request.Email,
            PasswordHash = HashPassword(request.Password),
            PhoneNumber = request.PhoneNumber,
            UserType = Enum.TryParse<UserType>(request.UserType, true, out var ut) ? ut : UserType.PersonalFinance,
            IsAdmin = isAdmin,
            IsEmailVerified = isAdmin
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        if (!isAdmin)
        {
            var code = await GenerateAndStoreVerificationCodeAsync(user.Id);
            await SendVerificationEmailAsync(user.Email, user.FullName, code);

            return new RegisterResponse
            {
                Message = "Account created. Please check your email for a verification code.",
                Email = request.Email,
                VerificationCode = code
            };
        }

        return new RegisterResponse
        {
            Message = "Admin account created successfully.",
            Email = request.Email
        };
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email)
            ?? throw new UnauthorizedAccessException("Invalid email or password.");

        if (!VerifyPassword(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid email or password.");

        if (!user.IsEmailVerified)
            throw new UnauthorizedAccessException("Please verify your email before logging in. Check your inbox for the verification code.");

        return GenerateTokenResponse(user);
    }

    public async Task<AuthResponse> VerifyEmailAsync(string email, string code)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email)
            ?? throw new InvalidOperationException("User not found.");

        if (user.IsEmailVerified)
            return GenerateTokenResponse(user);

        var verification = await _context.EmailVerifications
            .FirstOrDefaultAsync(v => v.UserId == user.Id && v.Code == code && !v.IsUsed && v.ExpiresAt > DateTime.UtcNow);

        if (verification == null)
            throw new InvalidOperationException("Invalid or expired verification code.");

        user.IsEmailVerified = true;
        user.UpdatedAt = DateTime.UtcNow;
        verification.IsUsed = true;

        await _context.SaveChangesAsync();

        return GenerateTokenResponse(user);
    }

    public async Task ResendVerificationAsync(string email)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email)
            ?? throw new InvalidOperationException("User not found.");

        if (user.IsEmailVerified)
            throw new InvalidOperationException("Email is already verified.");

        var code = await GenerateAndStoreVerificationCodeAsync(user.Id);
        await SendVerificationEmailAsync(user.Email, user.FullName, code);
    }

    public async Task<UserDto> GetUserByIdAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId)
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

    private async Task<string> GenerateAndStoreVerificationCodeAsync(int userId)
    {
        var random = new Random();
        var code = random.Next(100000, 999999).ToString();

        var verification = new EmailVerification
        {
            UserId = userId,
            Code = code,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            IsUsed = false
        };

        _context.EmailVerifications.Add(verification);
        await _context.SaveChangesAsync();

        return code;
    }

    private async Task SendVerificationEmailAsync(string email, string fullName, string code)
    {
        var subject = "Verify your Akanti account";
        var htmlBody = $@"
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

        await _emailService.SendAsync(email, subject, htmlBody);
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

    public async Task ForgotPasswordAsync(string email)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null) return;

        var cooldownMinutes = 10;
        var recentToken = await _context.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.CreatedAt > DateTime.UtcNow.AddMinutes(-cooldownMinutes))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        if (recentToken != null)
        {
            var waitSeconds = (int)(recentToken.CreatedAt.AddMinutes(cooldownMinutes) - DateTime.UtcNow).TotalSeconds;
            throw new InvalidOperationException($"A code was already sent. Please wait {waitSeconds / 60}m {waitSeconds % 60}s before requesting a new one.");
        }

        var random = new Random();
        var code = random.Next(100000, 999999).ToString();

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

        var subject = "Akanti - Password Reset Code";
        var htmlBody = $@"
            <div style='font-family: Arial, sans-serif; max-width: 500px; margin: 0 auto; padding: 20px;'>
                <h2 style='color: #16a34a;'>Password Reset Request</h2>
                <p>Hi {user.FullName},</p>
                <p>We received a request to reset your password. Use the following code:</p>
                <div style='background: #f3f4f6; border-radius: 8px; padding: 20px; text-align: center; margin: 20px 0;'>
                    <span style='font-size: 32px; font-weight: bold; letter-spacing: 8px; color: #16a34a;'>{code}</span>
                </div>
                <p style='color: #6b7280; font-size: 14px;'>This code expires in 15 minutes.</p>
                <p style='color: #6b7280; font-size: 14px;'>If you did not request a password reset, you can ignore this email.</p>
            </div>";

        await _emailService.SendAsync(user.Email, subject, htmlBody);
    }

    public async Task VerifyResetCodeAsync(string email, string code)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email)
            ?? throw new InvalidOperationException("User not found.");

        var resetToken = await _context.PasswordResetTokens
            .FirstOrDefaultAsync(t => t.UserId == user.Id && t.Code == code && !t.IsUsed && t.ExpiresAt > DateTime.UtcNow)
            ?? throw new InvalidOperationException("Invalid or expired reset code.");
    }

    public async Task ResetPasswordAsync(string email, string code, string newPassword)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email)
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
}
