using BeatLink.Models;

namespace BeatLink.Web;

/// <summary>How a login attempt ended (I5). The UI can show exactly what happened.</summary>
public enum LoginOutcome
{
    /// <summary>A token was obtained and stored.</summary>
    Success,

    /// <summary>The user cancelled, or the flow timed out waiting for the browser.</summary>
    Cancelled,

    /// <summary>The user declined consent at the provider (<c>error=access_denied</c>).</summary>
    Denied,

    /// <summary>Something failed: port in use, network down, bad token response, etc.</summary>
    Failed,
}

/// <summary>The result of a login attempt (I5, B2, B11).</summary>
public sealed record LoginResult(
    LoginOutcome Outcome,
    TokenResponse? Token = null,
    string? Message = null)
{
    public bool IsSuccess => Outcome == LoginOutcome.Success && Token is not null;

    public static LoginResult Success(TokenResponse token) => new(LoginOutcome.Success, token);
    public static LoginResult Cancelled(string? message = null) => new(LoginOutcome.Cancelled, Message: message);
    public static LoginResult Denied(string? message = null) => new(LoginOutcome.Denied, Message: message);
    public static LoginResult Failed(string message) => new(LoginOutcome.Failed, Message: message);
}
