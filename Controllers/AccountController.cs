using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Remus.Mvc.Data;
using Remus.Mvc.Models;

namespace Remus.Mvc.Controllers;

/// <summary>
/// Sign in and sign out.
/// </summary>
/// <remarks>
/// ============================================================================
/// WHAT A CONTROLLER IS, IN DJANGO TERMS
///
/// This class is `views.py`. MVC calls the request handler a *Controller* and
/// the template a *View*; Django calls the request handler a *View* and the
/// template a *Template*. Same architecture, swapped vocabulary — it is the
/// single most confusing thing when crossing between the two.
///
///     Django views.py  ==  this file
///     Django template  ==  Views/Account/Login.cshtml
///
/// One class groups related actions, the way one module groups related views.
/// Each public method is an "action" and, by convention, maps to a URL:
///
///     AccountController.Login()  ->  /Account/Login
///
/// from the single route template in Program.cs. Django would have needed a
/// path() entry for each.
///
/// Written by hand on purpose. `dotnet new mvc --auth Individual` would have
/// scaffolded Identity's Default UI — which is RAZOR PAGES, not MVC — and this
/// class would not exist. That is the equivalent of
/// `include("django.contrib.auth.urls")`: convenient, and you learn nothing.
/// ============================================================================
/// </remarks>
// ============================================================================
// NOTE: there is deliberately NO class-level [AllowAnonymous] here.
//
// The first draft had one, and the ASP0026 analyzer rejected it:
//
//     This [Authorize] attribute is overridden by an [AllowAnonymous]
//     attribute from farther away on 'AccountController'.
//
// [AllowAnonymous] anywhere in the chain WINS, no matter how far away — it is
// not "nearest wins" like CSS specificity. A class-level [AllowAnonymous]
// would therefore have silently un-protected the [Authorize] Logout action.
//
// Django cannot have this bug: @login_required decorates one view, and there
// is no enclosing scope that can cancel it.
// ============================================================================
public class AccountController(
    SignInManager<ApplicationUser> signInManager,
    ILogger<AccountController> logger) : Controller
{
    // ------------------------------------------------------------------
    // GET /Account/Login
    //
    // Django:
    //     def login_view(request):
    //         if request.method == "POST": ...
    //         form = LoginForm()
    //         return render(request, "account/login.html", {"form": form})
    //
    // MVC splits the two branches into two METHODS WITH THE SAME NAME,
    // distinguished by [HttpGet] / [HttpPost]. C# overloading plus attribute
    // routing does what Django expresses with `if request.method`.
    //
    // IActionResult is the return type for "some HTTP response": View(),
    // RedirectToAction(), NotFound(), Challenge(), File()... Django returns
    // HttpResponse subclasses for exactly the same reason.
    // ------------------------------------------------------------------
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        // Parameters are model-bound from the query string by name:
        // /Account/Login?returnUrl=/Profile  ->  returnUrl = "/Profile"
        // Django: request.GET.get("next")
        //
        // ViewData is the untyped bag that reaches the view. It IS Django's
        // context dict — string keys, object values, no compile-time checking.
        // Prefer the typed model (below) for anything substantial.
        ViewData["ReturnUrl"] = returnUrl;

        // View() finds Views/Account/Login.cshtml by convention (controller
        // name + action name) and renders it with this model.
        // Django: render(request, "account/login.html", {"form": form})
        return View(new LoginViewModel());
    }

    // ------------------------------------------------------------------
    // POST /Account/Login
    // ------------------------------------------------------------------
    [HttpPost]
    [AllowAnonymous]
    // Django: {% csrf_token %} + CsrfViewMiddleware. Here the token is emitted
    // automatically by the <form asp-action> tag helper, but VALIDATING it is
    // opt-in per action. Forgetting this attribute is a real CSRF hole —
    // Django is safe by default and you opt OUT with @csrf_exempt.
    // (Applying [AutoValidateAntiforgeryToken] globally flips MVC to Django's
    // safer default; left explicit here so the mechanism is visible.)
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;

        // MODEL BINDING already happened: MVC matched form field names to the
        // properties of LoginViewModel and ran every DataAnnotations attribute,
        // recording failures in ModelState.
        //
        // Django: form = LoginForm(request.POST); form.is_valid()
        //
        // The ordering differs in a way worth noticing. Django constructs the
        // form explicitly, so binding is visible in your code. MVC does it
        // before your method body runs, so ModelState is simply *already*
        // populated when you arrive.
        if (!ModelState.IsValid)
        {
            // Re-render with errors. No redirect: a redirect would discard the
            // user's input and the error messages.
            // Django: return render(request, "...", {"form": form})
            return View(model);
        }

        // PasswordSignInAsync does the whole dance: find the user, verify the
        // hash, check lockout and confirmation, then WRITE THE AUTH COOKIE.
        //
        // Django splits this into authenticate() + login(request, user).
        // Identity fuses them and returns a richer result.
        var result = await signInManager.PasswordSignInAsync(
            model.Email,
            model.Password,
            isPersistent: model.RememberMe,   // Django: request.session.set_expiry()
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            logger.LogInformation("{Email} signed in.", model.Email);
            return RedirectToLocal(returnUrl);
        }

        if (result.IsLockedOut)
        {
            logger.LogWarning("{Email} is locked out.", model.Email);
            ModelState.AddModelError(string.Empty,
                "This account is locked after too many failed attempts. Try again later.");
            return View(model);
        }

        if (result.IsNotAllowed)
        {
            // Raised because Program.cs sets SignIn.RequireConfirmedAccount.
            ModelState.AddModelError(string.Empty,
                "You must confirm your email address before signing in.");
            return View(model);
        }

        if (result.RequiresTwoFactor)
        {
            // Out of scope for this project — the Blazor app has the full flow.
            ModelState.AddModelError(string.Empty,
                "This account requires two-factor authentication, which this app does not implement. " +
                "Sign in through the Blazor app instead.");
            return View(model);
        }

        // Deliberately vague: never reveal whether the email exists.
        // ModelState.AddModelError(string.Empty, ...) is a FORM-LEVEL error —
        // Django's form.add_error(None, "..."), rendered by the validation
        // summary rather than next to a field.
        ModelState.AddModelError(string.Empty, "Incorrect email or password.");
        return View(model);
    }

    // ------------------------------------------------------------------
    // POST /Account/Logout
    //
    // POST, not GET, and [Authorize] so an anonymous request cannot trigger it.
    // A GET logout can be fired by any <img src="/Account/Logout"> on any page
    // on the internet. Django made LogoutView POST-only in 4.1 for this exact
    // reason.
    // ------------------------------------------------------------------
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var email = User.Identity?.Name;

        // Django: logout(request)
        await signInManager.SignOutAsync();
        logger.LogInformation("{Email} signed out.", email);

        // TempData survives exactly one redirect, then evaporates. It is
        // django.contrib.messages — and, like messages, it is backed by a
        // cookie or the session rather than by the response itself.
        TempData["Status"] = "You have been signed out.";

        // Django: redirect("home")
        return RedirectToAction(nameof(HomeController.Index), "Home");
    }

    // ------------------------------------------------------------------
    // GET /Account/AccessDenied
    //
    // Where ConfigureApplicationCookie sends a user who IS signed in but lacks
    // the required role. Django collapses both cases into a redirect to
    // LOGIN_URL, which is confusing when you are already logged in.
    // ------------------------------------------------------------------
    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    /// <summary>
    /// Redirects to <paramref name="returnUrl"/>, but only if it is local.
    /// </summary>
    /// <remarks>
    /// ================================================================
    /// OPEN REDIRECT PROTECTION. Do not skip this.
    ///
    /// returnUrl arrives from the query string, so an attacker can send
    /// someone to:
    ///
    ///     /Account/Login?returnUrl=https://evil.example/login
    ///
    /// They see your real domain, log in for real, and are then bounced to a
    /// convincing fake. Url.IsLocalUrl rejects anything with a scheme or host.
    ///
    /// Django's equivalent is url_has_allowed_host_and_scheme(), which
    /// LoginView calls on `next` for you. In MVC it is your job, in every
    /// action that honours a return URL.
    /// ================================================================
    /// </remarks>
    private IActionResult RedirectToLocal(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction(nameof(ProfileController.Index), "Profile");
}
