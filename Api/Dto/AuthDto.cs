using System.ComponentModel.DataAnnotations;

namespace Remus.Mvc.Api.Dto;

/// <summary>What POST /api/auth/login accepts. Django: the AuthenticationForm.</summary>
public sealed record LoginRequestDto
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; init; } = "";

    [Required(ErrorMessage = "Password is required.")]
    public string Password { get; init; } = "";

    /// <summary>Django: request.session.set_expiry(). Survives a browser restart.</summary>
    public bool RememberMe { get; init; }
}

/// <summary>
/// Who the caller is, as far as the server is concerned.
/// </summary>
/// <remarks>
/// GET /api/auth/me is the SPA's answer to a question a server-rendered page
/// never has to ask. _Layout.cshtml just writes `@User.Identity?.Name` while
/// rendering, because the server already knows. Angular boots as an empty page
/// in a browser and has to ASK - one round trip before it can decide whether to
/// show the profile or the login form.
///
/// That single extra request is most of what "SPA authentication" means.
///
/// Note it is [AllowAnonymous] and answers 200 with IsAuthenticated=false for a
/// signed-out caller, rather than 401. A 401 would also work, but every browser
/// console would show a red error on every cold load of a signed-out app, and
/// you would learn to ignore red errors. That habit costs more than the 401
/// saves.
/// </remarks>
public sealed record AuthStateDto
{
    public required bool IsAuthenticated { get; init; }
    public string? Email { get; init; }

    /// <summary>
    /// Read from the cookie's claims, NOT from AspNetUserRoles.
    ///
    /// So these are the roles as of the last sign-in. Change someone's roles in
    /// the database and this keeps reporting the old set until they sign in
    /// again - the same staleness useful_command warns about. Django re-reads
    /// the user row on every request and never has this problem; it pays a
    /// query per request for the privilege.
    /// </summary>
    public string[] Roles { get; init; } = [];

    public static AuthStateDto Anonymous => new() { IsAuthenticated = false };
}
