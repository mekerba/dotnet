using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Remus.Mvc.Api.Dto;
using Remus.Mvc.Data;

namespace Remus.Mvc.Api;

// ============================================================================
// GET /api/profile   ·   PUT /api/profile
//
// The same two operations as ProfileController's Index and Edit, for a caller
// that wants data instead of a page. Same DbContext, same auth cookie, same
// [Authorize] rule - only the renderer moved into the browser.
//
// ---------------------------------------------------------------------------
// MINIMAL API vs. THE CONTROLLER NEXT DOOR
//
//   Controllers/ProfileController.cs        this file
//   ────────────────────────────────        ──────────────────────────────────
//   URL implied by class + method name      URL written out: MapGet("/")
//   found by MVC's convention scan          registered by a call in Program.cs
//   : Controller (base class, ~30 members)  a static method, no base class
//   db injected into the constructor        db injected per PARAMETER
//   [Authorize] attribute                   .RequireAuthorization() on the group
//   ModelState.IsValid, checked by hand     AddValidation(), checked for you
//
// The first row is the one that matters for a Django developer. Program.cs
// complains that MVC's convention routing hides the URLs - "renaming
// ProfileController silently changes your URLs". Minimal APIs give urls.py
// back: the route is a string, in a file, that you can grep for.
//
// The trade is real in both directions. The controller groups related actions
// in a class and shares state through its constructor; these are loose
// functions that re-declare their dependencies every time. Past a dozen
// endpoints that repetition starts to hurt, which is when people either adopt
// a convention like this file or go back to [ApiController]. Neither is wrong.
// ---------------------------------------------------------------------------
// ============================================================================

public static class ProfileEndpoints
{
    public static RouteGroupBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        // Nested inside the group built by ApiSetup.MapApi, so the prefix here
        // is relative: "/api" + "/profile".
        var group = app
            .MapGroup("/profile")
            .RequireAuthorization()   // Django: @method_decorator(login_required)
            .WithTags("Profile");     // groups these under one heading in OpenAPI

        group.MapGet("", GetProfile)
            .WithName("GetProfile")
            .WithSummary("The signed-in user's profile.");

        group.MapPut("", UpdateProfile)
            .WithName("UpdateProfile")
            .WithSummary("Replace the signed-in user's editable profile fields.")
            // The write, and the only endpoint here that needs a CSRF token.
            // A GET changes nothing, so it does not.
            .AddEndpointFilter<AntiforgeryFilter>();

        return group;
    }

    // ------------------------------------------------------------------
    // GET /api/profile
    // ------------------------------------------------------------------
    /// <remarks>
    /// EVERY PARAMETER COMES FROM DEPENDENCY INJECTION OR THE REQUEST, and the
    /// framework works out which without being told:
    ///
    ///   ClaimsPrincipal       special-cased -> HttpContext.User
    ///   ApplicationDbContext  registered in DI -> the request's scoped instance
    ///   CancellationToken     special-cased -> HttpContext.RequestAborted
    ///
    /// A controller would have taken the first two through its constructor.
    /// Same objects, same lifetimes; the difference is only where you declare
    /// them. Django's equivalent of the DbContext parameter is `from .models
    /// import Profile` - a module-level import rather than an injected object,
    /// which is why Django code is harder to test against a fake database.
    ///
    /// RETURN TYPE: Results&lt;Ok&lt;ProfileDto&gt;, NotFound&gt; is not
    /// decoration. IActionResult / IResult would compile just as well, but then
    /// the OpenAPI document - and therefore the TypeScript client generated
    /// from it - could only say "returns something". Spelling the union out
    /// gives Angular a typed 200 body and a documented 404. It is the closest
    /// thing .NET has to DRF's @extend_schema(responses={...}), except the
    /// compiler checks it.
    /// </remarks>
    private static async Task<Results<Ok<ProfileDto>, NotFound>> GetProfile(
        ClaimsPrincipal user,
        ApplicationDbContext db,
        CancellationToken ct)
    {
        // Scoped to the CLAIM, never to an id from the query string. An
        // endpoint taking ?userId= would let any signed-in visitor read anyone
        // else's profile - the note on ProfileController.Summary spells this
        // out, and it is worth repeating because an API makes it so easy.
        var profile = await db.Profiles
            .AsNoTracking()                 // read-only: no change tracking needed
            .Include(p => p.User)           // Django: .select_related("user")
            .FirstOrDefaultAsync(p => p.UserId == user.CurrentUserId(), ct);

        return profile is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ProfileDto.FromEntity(profile.User, profile));
    }

    // ------------------------------------------------------------------
    // PUT /api/profile
    // ------------------------------------------------------------------
    /// <remarks>
    /// PUT, NOT POST, and there is no redirect afterwards.
    ///
    /// ProfileController.Edit ends with RedirectToAction - the POST/REDIRECT/GET
    /// dance, so that a refresh does not re-submit. That problem does not exist
    /// here: fetch() does not put anything in the address bar and F5 reloads the
    /// Angular app, not this request. The SPA replaced a browser behaviour with
    /// one it controls, which is the trade it always makes.
    ///
    /// PUT because the body carries the complete set of editable fields and
    /// replaces them wholesale. A PATCH taking only changed fields would need a
    /// way to distinguish "set this to null" from "leave this alone" - which
    /// C#'s nullable types cannot express on their own, and is why half-built
    /// PATCH endpoints quietly wipe columns.
    ///
    /// Validation already ran (see ProfileUpdateDto). If it had failed, this
    /// method was never called.
    /// </remarks>
    private static async Task<Results<Ok<ProfileDto>, NotFound>> UpdateProfile(
        ProfileUpdateDto input,        // bound from the JSON body, then validated
        ClaimsPrincipal user,
        ApplicationDbContext db,
        ILogger<ProfileUpdateDto> logger,
        CancellationToken ct)
    {
        // NOT AsNoTracking: change tracking is the mechanism that turns the
        // mutations in ApplyTo into an UPDATE. It replaces Django's .save().
        var profile = await db.Profiles
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == user.CurrentUserId(), ct);

        if (profile is null) return TypedResults.NotFound();

        input.ApplyTo(profile);

        // No db.Update() call. The context knows which properties changed and
        // emits an UPDATE touching only those columns. LastUpdate is stamped by
        // the SaveChangesAsync override on the DbContext - this project's
        // stand-in for auto_now.
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Profile updated for {UserId} via the API.", profile.UserId);

        // Return the saved row rather than 204 No Content. The client then does
        // not have to guess what the server did to its input - Country was
        // upper-cased, whitespace was trimmed, LastUpdate moved - and the
        // Angular form can just overwrite itself with the truth.
        return TypedResults.Ok(ProfileDto.FromEntity(profile.User, profile));
    }
}
