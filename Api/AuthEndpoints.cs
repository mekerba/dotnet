using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Remus.Mvc.Api.Dto;
using Remus.Mvc.Data;

namespace Remus.Mvc.Api;

// ============================================================================
// POST /api/auth/login   ·   POST /api/auth/logout   ·   GET /api/auth/me
//
// AccountController, minus the views.
//
// ---------------------------------------------------------------------------
// THIS STILL USES THE COOKIE. There is no JWT anywhere in this project.
//
// SignInManager.PasswordSignInAsync writes the very same .Remus.Mvc.Auth cookie
// the MVC login page writes, so a visitor signed in at localhost:5260/Account/
// Login is signed in to the Angular app as well, and vice versa. One session,
// two front ends.
//
// That is a deliberate choice, and the opposite of what most Angular tutorials
// do. Cookies are worth staying on as long as the API and the client are the
// same site, because the browser manages the credential for you: it is HttpOnly
// so no script can steal it, it expires on its own, and signing out actually
// signs you out. A JWT in localStorage has none of those properties and buys
// you exactly one thing - the ability to be called from a DIFFERENT site. You
// are not calling from a different site; proxy.conf.json in the Angular project
// exists to make sure of it.
//
// Reach for tokens when you genuinely have a third party, a mobile app, or an
// API with no browser in front of it. Until then this is less code and more
// security. (.NET's token answer, when you need it, is
// AddIdentityApiEndpoints<ApplicationUser>() + app.MapIdentityApi<T>(), which
// hands you register/login/refresh/2FA endpoints in two lines.)
// ---------------------------------------------------------------------------
// ============================================================================

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // No .RequireAuthorization() on this group - you cannot require a
        // sign-in from the endpoint whose job is to sign you in. Each endpoint
        // below states its own rule instead.
        var group = app
            .MapGroup("/auth")
            .WithTags("Auth");

        group.MapGet("/me", Me)
            .WithName("AuthMe")
            .WithSummary("Who the caller is. Answers 200 for anonymous callers too.");

        group.MapPost("/login", Login)
            .WithName("AuthLogin")
            .WithSummary("Sign in and receive the auth cookie.")
            .AddEndpointFilter<AntiforgeryFilter>();

        group.MapPost("/logout", Logout)
            .RequireAuthorization()
            .WithName("AuthLogout")
            .WithSummary("Clear the auth cookie.")
            .AddEndpointFilter<AntiforgeryFilter>();

        return group;
    }

    // ------------------------------------------------------------------
    // GET /api/auth/me
    // ------------------------------------------------------------------
    /// <remarks>
    /// Free. No database is touched: every value below was decrypted out of the
    /// cookie that arrived with the request.
    ///
    /// Django cannot do this. AuthenticationMiddleware re-fetches the user row
    /// on every single request, so request.user is always current and always
    /// costs a query. Read AuthStateDto.Roles for what .NET pays for the
    /// saving.
    ///
    /// It also doubles as the call that primes the XSRF-TOKEN cookie, because
    /// it is a GET under /api - see ApiSetup.UseApiAntiforgeryCookie.
    /// </remarks>
    private static Ok<AuthStateDto> Me(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return TypedResults.Ok(AuthStateDto.Anonymous);

        return TypedResults.Ok(new AuthStateDto
        {
            IsAuthenticated = true,
            Email = user.FindFirstValue(ClaimTypes.Name) ?? user.FindFirstValue(ClaimTypes.Email),
            Roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray(),
        });
    }

    // ------------------------------------------------------------------
    // POST /api/auth/login
    // ------------------------------------------------------------------
    /// <remarks>
    /// The body of AccountController.Login, with every `return View(model)`
    /// replaced by a status code. Worth reading the two side by side: the
    /// decisions are identical and only the output differs.
    ///
    /// ---------------------------------------------------------------------
    /// EVERY FAILURE ANSWERS 401 WITH THE SAME TEXT - ON PURPOSE.
    ///
    /// The controller is more talkative: it distinguishes "locked out" from
    /// "confirm your email" from "wrong password", because a human at a form
    /// needs to know which. An open API endpoint is a different situation. Each
    /// distinct message tells an unauthenticated caller something about an
    /// account that may not be theirs - "this email is locked out" confirms the
    /// email exists, and confirming which emails exist is how credential
    /// stuffing lists get built.
    ///
    /// So the detail is logged server-side and the caller gets one answer.
    /// Django's AuthenticationForm makes the same call ("Please enter a correct
    /// username and password") for the same reason.
    ///
    /// The lockout itself still happens - lockoutOnFailure: true - the attacker
    /// just is not told about it.
    /// ---------------------------------------------------------------------
    /// </remarks>
    private static async Task<Results<Ok<AuthStateDto>, ProblemHttpResult>> Login(
        LoginRequestDto input,
        SignInManager<ApplicationUser> signInManager,
        ILogger<LoginRequestDto> logger)
    {
        var result = await signInManager.PasswordSignInAsync(
            input.Email,
            input.Password,
            isPersistent: input.RememberMe,
            lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            logger.LogWarning(
                "API sign-in refused for {Email}. LockedOut={LockedOut} NotAllowed={NotAllowed} TwoFactor={TwoFactor}",
                input.Email, result.IsLockedOut, result.IsNotAllowed, result.RequiresTwoFactor);

            return TypedResults.Problem(
                title: "Sign-in failed.",
                detail: "Incorrect email or password, or the account cannot sign in.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        logger.LogInformation("{Email} signed in via the API.", input.Email);

        // PasswordSignInAsync has already appended Set-Cookie to this response.
        // Nothing else is returned to the client as a credential - the body
        // below is just information, and could be dropped entirely at the cost
        // of one extra GET /api/auth/me from the client.
        //
        // signInManager.Context.User is NOT yet the new user: HttpContext.User
        // is only rebuilt on the NEXT request, once the cookie comes back. So
        // the roles are read from the sign-in, not from the principal.
        var user = await signInManager.UserManager.FindByEmailAsync(input.Email);
        var roles = user is null ? [] : await signInManager.UserManager.GetRolesAsync(user);

        return TypedResults.Ok(new AuthStateDto
        {
            IsAuthenticated = true,
            Email = user?.Email,
            Roles = [.. roles],
        });
    }

    // ------------------------------------------------------------------
    // POST /api/auth/logout
    // ------------------------------------------------------------------
    /// <remarks>
    /// POST, never GET - identical to AccountController.Logout and to Django,
    /// which made LogoutView POST-only in 4.1. A GET logout can be triggered by
    /// any &lt;img src="/api/auth/logout"&gt; on any page on the internet.
    /// Harmless as attacks go, extremely annoying as a bug report.
    /// </remarks>
    private static async Task<NoContent> Logout(
        SignInManager<ApplicationUser> signInManager,
        ClaimsPrincipal user,
        ILogger<AuthStateDto> logger)
    {
        logger.LogInformation("{Email} signed out via the API.", user.Identity?.Name);

        // Deletes the cookie. There is no server-side session to invalidate -
        // the cookie WAS the session. Django would delete a row from
        // django_session here.
        await signInManager.SignOutAsync();

        return TypedResults.NoContent();
    }
}
