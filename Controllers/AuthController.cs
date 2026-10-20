using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Akanti.API.DTOs.Auth;
using Akanti.API.Exceptions;
using Akanti.API.Interfaces;

namespace Akanti.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    [EnableRateLimiting("auth-register")]
    public async Task<ActionResult<RegisterResponse>> Register([FromBody] RegisterRequest request)
    {
        try
        {
            var response = await _authService.RegisterAsync(request);
            return Ok(response);
        }
        catch (AuthFlowException)
        {
            // Deliberately uniform: an already-registered email looks identical to a
            // successful registration so the endpoint cannot be used to enumerate accounts.
            return Ok(new RegisterResponse
            {
                Message = "Account created. Please check your email for a verification code.",
                Email = request.Email
            });
        }
        catch (Exception)
        {
            // Do not echo internal exception details to the client.
            return StatusCode(500, new { message = "An unexpected error occurred during registration." });
        }
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth-login")]
    public async Task<ActionResult<object>> Login([FromBody] LoginRequest request)
    {
        try
        {
            var response = await _authService.LoginAsync(request);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var message = ex.Message;
            var needsVerification = message.Contains("verify your email");
            return Unauthorized(new { message, needsVerification, email = needsVerification ? request.Email : null });
        }
    }

    [HttpPost("verify-email")]
    [EnableRateLimiting("auth-verify")]
    public async Task<ActionResult<AuthResponse>> VerifyEmail([FromBody] VerifyEmailRequest request)
    {
        try
        {
            var response = await _authService.VerifyEmailAsync(request.Email, request.Code);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("resend-code")]
    [EnableRateLimiting("auth-codes")]
    public async Task<IActionResult> ResendCode([FromBody] ResendCodeRequest request)
    {
        try
        {
            await _authService.ResendVerificationAsync(request.Email);
            return Ok(new { message = "Verification code sent. Please check your email." });
        }
        catch (InvalidOperationException ex)
        {
            // A cooldown message is safe to surface; an unknown email is not,
            // so it collapses into the same success message as a real send.
            if (ex.Message.Contains("wait", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = ex.Message });

            return Ok(new { message = "Verification code sent. Please check your email." });
        }
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> GetCurrentUser()
    {
        try
        {
            var userIdClaim = User.FindFirst("sub")
                              ?? User.FindFirst("id")
                              ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);

            if (userIdClaim == null)
            {
                return Unauthorized(new { message = "User ID claim not found in token." });
            }

            if (!int.TryParse(userIdClaim.Value, out int userId))
            {
                return BadRequest(new { message = $"Failed to parse User ID '{userIdClaim.Value}' as an integer." });
            }

            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            return Ok(user);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Auth Error] Error retrieving current user: {ex.Message}");
            return StatusCode(500, new { message = "An internal error occurred while retrieving user details." });
        }
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth-codes")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        try
        {
            await _authService.ForgotPasswordAsync(request.Email);
            return Ok(new { message = "If an account with that email exists, a reset code has been sent." });
        }
        catch (InvalidOperationException ex)
        {
            // Keep the response identical whether or not the account exists; only the
            // cooldown (which describes the caller's own actions) is safe to expose.
            if (ex.Message.Contains("wait", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = ex.Message });

            return Ok(new { message = "If an account with that email exists, a reset code has been sent." });
        }
    }

    [HttpPost("verify-reset-code")]
    [EnableRateLimiting("auth-verify")]
    public async Task<IActionResult> VerifyResetCode([FromBody] VerifyResetCodeRequest request)
    {
        try
        {
            await _authService.VerifyResetCodeAsync(request.Email, request.Code);
            return Ok(new { message = "Code verified. You can now reset your password." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("auth-verify")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        try
        {
            await _authService.ResetPasswordAsync(request.Email, request.Code, request.NewPassword);
            return Ok(new { message = "Password has been reset successfully." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [Authorize]
    [HttpPost("change-password")]
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        try
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
                              ?? User.FindFirst("sub")
                              ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);

            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                return Unauthorized(new { message = "User not found." });

            await _authService.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword);
            return Ok(new { message = "Password changed successfully." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
