using Akanti.API.DTOs.Auth;

namespace Akanti.API.Services;

public interface IAuthService
{
    Task<RegisterResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<UserDto> GetUserByIdAsync(int userId);
    Task ForgotPasswordAsync(string email);
    Task VerifyResetCodeAsync(string email, string code);
    Task ResetPasswordAsync(string email, string code, string newPassword);
    Task<AuthResponse> VerifyEmailAsync(string email, string code);
    Task ResendVerificationAsync(string email);
    Task ChangePasswordAsync(int userId, string currentPassword, string newPassword);
}
