using System.ComponentModel.DataAnnotations;

namespace Akanti.API.DTOs.Auth;

public class VerifyEmailRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, Length(6, 6)]
    public string Code { get; set; } = string.Empty;
}

public class ResendCodeRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
}
