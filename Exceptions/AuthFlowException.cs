namespace Akanti.API.Exceptions;

/// <summary>
/// Thrown when an authentication flow completes in a way the caller should not be
/// told the specifics of (e.g. registering an email that already exists).
/// The controller turns this into a generic, uniform response so the API cannot be
/// used to enumerate which email addresses have accounts.
/// </summary>
public class AuthFlowException : Exception
{
    public AuthFlowException(string message) : base(message) { }
}
