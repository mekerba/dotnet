using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;

namespace Remus.Mvc.Api;

// ============================================================================
// The wiring that every /api endpoint shares.
//
// Program.cs calls three things from this file:
//
//     app.UseApiAntiforgeryCookie();   in the pipeline, before the endpoints
//     app.MapApi();                    next to MapControllerRoute
//
// plus the AntiforgeryFilter below, attached per endpoint.
//
// Django gets all of this from one line in MIDDLEWARE (CsrfViewMiddleware) and
// one in urls.py (include("api.urls")). Here it is explicit, which is more
// typing and considerably easier to reason about at 2am.
// ============================================================================

public static class ApiSetup
{
    /// <summary>Everything under this prefix behaves like an API, not a web page.</summary>
    /// <remarks>
    /// Used in three places and worth having as a constant, because getting it
    /// wrong in any one of them fails silently:
    ///   1. this file's route group,
    ///   2. Program.cs's cookie events (401 instead of 302),
    ///   3. remus-angular/proxy.conf.json.
    /// </remarks>
    public const string Prefix = "/api";

    // ------------------------------------------------------------------
    // Django: urls.py's include("api.urls")
    // ------------------------------------------------------------------
    /// <summary>Mounts every API endpoint under /api.</summary>
    /// <remarks>
    /// MapGroup is the piece that makes minimal APIs scale past a toy. Anything
    /// applied to the group - a prefix, .RequireAuthorization(), a filter,
    /// OpenAPI metadata - applies to every endpoint inside it, including ones
    /// added later by someone who never read this file. It is Django's
    /// `path("api/", include(...))` plus a decorator applied to the whole
    /// module at once.
    /// </remarks>
    public static IEndpointRouteBuilder MapApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup(Prefix);

        api.MapAuthEndpoints();
        api.MapProfileEndpoints();

        return app;
    }

    // ------------------------------------------------------------------
    // CSRF, the half that has to reach the browser
    // ------------------------------------------------------------------
    /// <summary>
    /// Issues the XSRF-TOKEN cookie that Angular's HttpClient looks for.
    /// </summary>
    /// <remarks>
    /// THE SHAPE OF THE PROBLEM, which is identical in Django:
    ///
    /// The auth cookie is attached by the browser to every request to this
    /// origin, including ones started by a form on someone else's site. So
    /// "the caller has a valid cookie" does NOT mean "the caller meant to send
    /// this". The defence is a second secret that a cross-site attacker cannot
    /// read: a token the server hands out, which the client has to echo back.
    ///
    /// The server half is IAntiforgery.ValidateRequestAsync - see
    /// AntiforgeryFilter below. This is the client half.
    ///
    ///     GetAndStoreTokens  writes the HttpOnly half as its own cookie AND
    ///                        returns RequestToken, the half the client echoes.
    ///     HttpOnly = false   is REQUIRED and is not a mistake: Angular's
    ///                        HttpClient has to be able to read it with
    ///                        document.cookie. It carries no authority by
    ///                        itself - it is useless without the HttpOnly half.
    ///
    /// Names: ASP.NET would call the header "RequestVerificationToken";
    /// Program.cs renames it to X-XSRF-TOKEN because that is what Angular sends
    /// by default. Django's pair is csrftoken / X-CSRFToken - same mechanism,
    /// three different spellings, which is the whole difficulty.
    ///
    /// WHY ONLY ON GET: a POST needs the cookie to ALREADY exist, so issuing it
    /// during a POST is too late. The Angular app calls GET /api/auth/me while
    /// it boots, which is what primes this. If you ever build a client that
    /// writes before it reads, give it a GET to call first.
    /// </remarks>
    public static IApplicationBuilder UseApiAntiforgeryCookie(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsGet(context.Request.Method) &&
                context.Request.Path.StartsWithSegments(Prefix))
            {
                var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
                var tokens = antiforgery.GetAndStoreTokens(context);

                context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
                {
                    HttpOnly = false,                  // deliberate - see above
                    SameSite = SameSiteMode.Strict,
                    Secure = context.Request.IsHttps,  // true under the https profile, false under http
                    Path = "/",
                });
            }

            await next();
        });

    // ------------------------------------------------------------------
    /// <summary>The signed-in user's id, from the auth cookie's claims.</summary>
    /// <remarks>
    /// The same property ProfileController has, moved to an extension method so
    /// the endpoints can share it. Read the long note on
    /// ProfileController.CurrentUserId for why a ClaimsPrincipal is not
    /// Django's request.user.
    /// </remarks>
    public static string CurrentUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("No user id claim on an authorized endpoint.");
}

// ============================================================================
/// <summary>Rejects a write whose antiforgery token is missing or wrong.</summary>
/// <remarks>
/// An ENDPOINT FILTER is the minimal-API equivalent of an MVC action filter -
/// so this class is [ValidateAntiForgeryToken], rewritten. It is Django's
/// CsrfViewMiddleware narrowed to the endpoints that opt in.
///
/// ---------------------------------------------------------------------------
/// WHY THIS IS NOT AUTOMATIC, which surprises everyone once.
///
/// app.UseAntiforgery() exists and Program.cs could call it - but that
/// middleware only validates endpoints that bind FORM data (IFormFile,
/// IFormCollection, [FromForm]). An endpoint taking a JSON body is not
/// validated by it. The reasoning is that a browser cannot send
/// `Content-Type: application/json` cross-origin without a CORS preflight, so
/// JSON is held to be safe by construction.
///
/// That reasoning is sound and this filter is still here, because "safe by
/// construction" lasts exactly until someone relaxes the CORS policy in a
/// hurry. Attaching it costs one line per write endpoint. Django protects every
/// unsafe method by default and makes you opt OUT with @csrf_exempt; that is
/// the better default, and this is as close to it as minimal APIs get.
/// ---------------------------------------------------------------------------
/// </remarks>
public sealed class AntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            // 400, not 403: nothing is wrong with WHO you are, only with the
            // request. Angular's interceptor must not treat this as "log in
            // again" - it means "you never called a GET first, so you have no
            // XSRF-TOKEN cookie to echo".
            return TypedResults.Problem(
                title: "Invalid or missing antiforgery token.",
                detail: "Send the XSRF-TOKEN cookie's value back in the X-XSRF-TOKEN header. "
                      + "Angular's HttpClient does this automatically once a GET has issued the cookie.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return await next(context);
    }
}
