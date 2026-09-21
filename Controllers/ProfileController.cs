using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Remus.Mvc.Data;
using Remus.Mvc.Models;

namespace Remus.Mvc.Controllers;

/// <summary>
/// View and edit the signed-in user's profile.
/// </summary>
/// <remarks>
/// Django:
///
///     @method_decorator(login_required, name="dispatch")
///     class ProfileView(...):
///
/// [Authorize] on the CLASS applies to every action, which is
/// `@method_decorator(login_required, name="dispatch")` or LoginRequiredMixin.
/// Anonymous visitors are sent to ConfigureApplicationCookie's LoginPath —
/// Django's LOGIN_URL — with ?returnUrl= carrying where they were going.
///
/// ---------------------------------------------------------------------------
/// ApplicationDbContext IS INJECTED DIRECTLY, and that is correct here.
///
/// In the Blazor project the equivalent work went through IProfileService on an
/// IDbContextFactory, because an interactive component's DI scope is the
/// SignalR circuit and one shared DbContext would be used concurrently.
///
/// A controller has no such problem. It is created per request, used on one
/// thread, and thrown away — precisely Django's model. So the plain scoped
/// DbContext is both simpler and right.
///
/// (Extracting a service layer is still worthwhile once logic is shared across
/// controllers. It is skipped here so the controller's own responsibilities
/// stay visible, which is the point of the exercise.)
/// ---------------------------------------------------------------------------
/// </remarks>
[Authorize]
public class ProfileController(
    ApplicationDbContext db,
    ILogger<ProfileController> logger) : Controller
{
    // ------------------------------------------------------------------
    // GET /Profile   (action name defaulted by the route template)
    // ------------------------------------------------------------------
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = CurrentUserId;

        // Include(p => p.User) is .select_related("user"): one query with a
        // JOIN. Without it p.User is null — EF does not lazily fetch the way
        // Django does.
        //
        // AsNoTracking because this is read-only. Django never tracks, so it
        // has no equivalent knob.
        var profile = await db.Profiles
            .AsNoTracking()
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (profile is null)
        {
            // Every user gets a profile at registration (in the Blazor app),
            // so this means the row was deleted by hand. Fail loudly.
            logger.LogError("Signed-in user {UserId} has no profile row.", userId);

            // Django: raise Http404 / return HttpResponseNotFound()
            return NotFound();
        }

        // Django: render(request, "users/profile.html", {"profile": profile})
        return View(ProfileDetailsViewModel.FromEntity(profile.User, profile));
    }

    // ------------------------------------------------------------------
    // GET /Profile/Edit   — render the form, populated from the database
    //
    // Django: form = ProfileForm(instance=profile)
    // ------------------------------------------------------------------
    [HttpGet]
    public async Task<IActionResult> Edit()
    {
        var userId = CurrentUserId;

        var profile = await db.Profiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (profile is null) return NotFound();

        return View(ProfileEditViewModel.FromEntity(profile));
    }

    // ------------------------------------------------------------------
    // POST /Profile/Edit
    //
    // Django:
    //     form = ProfileForm(request.POST, instance=profile)
    //     if form.is_valid():
    //         form.save()
    //         return redirect("profile")
    //     return render(request, "users/profile_form.html", {"form": form})
    // ------------------------------------------------------------------
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ProfileEditViewModel model)
    {
        // Binding and validation already ran — see the note in AccountController.
        if (!ModelState.IsValid)
        {
            // Re-render the SAME view with the SAME model, so the user's typing
            // survives and asp-validation-for has errors to show.
            // Django: return render(request, "...", {"form": form})
            return View(model);
        }

        var userId = CurrentUserId;

        // NOT AsNoTracking this time. Change tracking is what turns the
        // mutations below into an UPDATE — it is the mechanism that replaces
        // Django's explicit .save().
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.UserId == userId);
        if (profile is null) return NotFound();

        model.ApplyTo(profile);

        // No db.Update(profile) call is needed: the context already knows which
        // properties changed and emits an UPDATE touching only those columns.
        // Django's .save() rewrites every column unless you pass update_fields.
        //
        // LastUpdate is stamped by the SaveChangesAsync override on the
        // DbContext — the project's stand-in for auto_now.
        await db.SaveChangesAsync();

        logger.LogInformation("Profile updated for {UserId}.", userId);

        // Django: messages.success(request, "...")
        TempData["Status"] = "Your profile has been saved.";

        // ================================================================
        // POST/REDIRECT/GET.
        //
        // Never return View() after a successful POST. The URL would still be
        // /Profile/Edit with a POST body, so a refresh re-submits and the
        // browser shows "Confirm Form Resubmission".
        //
        // Django developers do this by reflex — `return redirect(...)` after a
        // successful form — and the reflex transfers unchanged.
        // ================================================================
        return RedirectToAction(nameof(Index));
    }

    // ------------------------------------------------------------------
    // GET /Profile/Summary   ->  JSON, for the Edit page's preview modal.
    // Django: a view that returns JsonResponse.
    // ------------------------------------------------------------------
    //
    // NOTHING NEW IS NEEDED TO SERVE JSON.
    //
    // Same Controller base class, same route template, same injected db as the
    // HTML actions above — only the return type differs. Json(x) sets the
    // Content-Type and serialises with System.Text.Json. There is no separate
    // "API project", no [ApiController], no second routing system.
    //
    // Three things to know about it:
    //
    //   * PROPERTY NAMES ARRIVE camelCase. AddControllersWithViews configures
    //     System.Text.Json with the web defaults, so FirstName is serialised
    //     as "firstName". The JavaScript reads the camelCase spelling. This
    //     catches everyone once.
    //
    //   * [Authorize] on the class covers this action too, so it is not
    //     public. But an EXPIRED cookie produces a 302 to the login page, not
    //     a 401 — and fetch() follows redirects silently, so the browser gets
    //     200 OK carrying an HTML login form where it expected JSON. The
    //     caller in profile-edit.js checks Content-Type for exactly this.
    //
    //   * This is a GET, so no antiforgery token is involved. A JSON endpoint
    //     that WRITES must be [HttpPost] and carry one — see the note at the
    //     bottom of profile-edit.js.
    //
    [HttpGet]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        var userId = CurrentUserId;

        // Scoped to the signed-in user, not to an id from the query string.
        // An endpoint that took ?userId= would let any signed-in visitor read
        // anyone's profile — the most common way an added AJAX endpoint
        // quietly becomes a data leak.
        var profile = await db.Profiles
            .AsNoTracking()
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (profile is null)
            return NotFound();

        // Projected explicitly rather than returning the entity, for the same
        // reason ProfileViewModel exists: serialising a model straight out of
        // the database publishes every column you later add to it, including
        // the ones you did not mean to. Django: a ModelSerializer with an
        // explicit `fields` list rather than `__all__`.
        //
        // .ToString() on the enums because System.Text.Json writes an enum as
        // its ORDINAL by default — the modal would show "0" instead of "Mr".
        // Done here in memory, after the query, where it is plain C#.
        return Json(new
        {
            Email = profile.User.Email,
            Title = profile.Title?.ToString(),
            profile.FirstName,
            profile.LastName,
            Gender = profile.Gender?.ToString(),
            profile.DateBirth,
            profile.PlaceBirth,
            profile.City,
            profile.Country,
            profile.Nationality,
            profile.Position,
            profile.Institution,
            profile.Biography,
            profile.IsValidated,
            profile.LastUpdate,
        });
    }

    /// <summary>
    /// The signed-in user's id, from the auth cookie's claims.
    /// </summary>
    /// <remarks>
    /// `User` here is HttpContext.User, a ClaimsPrincipal — and it is NOT
    /// Django's request.user.
    ///
    ///   Django  : request.user IS a User row, re-fetched from the database on
    ///             every request by AuthenticationMiddleware. Always current,
    ///             always costs a query.
    ///   ASP.NET : a bag of claims decrypted from the cookie. Free, but it can
    ///             be stale — change someone's roles and their existing cookie
    ///             still carries the old ones until they sign in again.
    ///
    /// So there is no User.Profile to follow. Take the id from the claim and
    /// query, which is what every action above does.
    /// </remarks>
    private string CurrentUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("No user id claim on an [Authorize] action.");
}
