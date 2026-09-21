using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Remus.Mvc.Api;
using Remus.Mvc.Commands;
using Remus.Mvc.Data;

// ============================================================================
// Program.cs  —  MVC edition
//
// Same role as in the Blazor project and as settings.py + urls.py in Django:
// register services, compose the middleware pipeline, map routes.
//
// Compare this file against ../remus_dotnet/src/Remus.Web/Program.cs. The
// differences are the whole lesson:
//
//   Blazor                                  MVC (here)
//   ─────────────────────────────────       ─────────────────────────────────
//   AddRazorComponents()                    AddControllersWithViews()
//     .AddInteractiveServerComponents()
//   AddIdentityCore<T>()                    AddIdentity<TUser, TRole>()
//     + AddAuthentication().AddIdentityCookies()   (cookies come included)
//   AddDbContextFactory + AddScoped         AddDbContext  (scoped is enough)
//   MapRazorComponents<App>()               MapControllerRoute(...)
//   routes declared per component (@page)   one central route template
// ============================================================================

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// MVC
// ---------------------------------------------------------------------------
// Registers controller activation, the Razor view engine, model binding,
// validation, tag helpers. "WithViews" is the part that adds the view engine —
// AddControllers() alone is for APIs with no HTML.
//
// Django: nothing to register. TEMPLATES and the URL resolver are always on.
builder.Services.AddControllersWithViews();

// ---------------------------------------------------------------------------
// THE API  —  Django: INSTALLED_APPS += ["rest_framework"]
// ---------------------------------------------------------------------------
// Everything under /api is served to the Angular app in ../remus-angular. The
// endpoints live in Api/; this is the whole of their registration.
//
// NOTE WHAT IS NOT HERE. No AddControllers(), no second routing system, no
// separate project, and — apart from the OpenAPI document — no NuGet package.
// Minimal APIs, model binding, JSON and validation all ship inside
// Microsoft.NET.Sdk.Web, which this project already used. DRF is a dependency
// you install; this is not.

// Serves the OpenAPI document at /openapi/v1.json.
// Django: drf-spectacular. Its real payoff is generating the Angular client.
builder.Services.AddOpenApi();

// New in .NET 10. Runs the DataAnnotations on a minimal API's body parameter
// BEFORE the handler is entered, answering 400 + ProblemDetails on failure —
// so an endpoint never needs the equivalent of ModelState.IsValid.
// Django: serializer.is_valid(raise_exception=True), except unforgettable.
builder.Services.AddValidation();

// ===========================================================================
// TWO JSON CONFIGURATIONS EXIST AND THEY ARE NOT THE SAME OBJECT.
//
//   ConfigureHttpJsonOptions                  -> minimal APIs   (Api/)
//   AddControllersWithViews().AddJsonOptions  -> controllers    (Controllers/)
//
// Both default to camelCase, so the two agree right up until the day you
// configure one and cannot work out why the other ignored you.
//
// The converter below is why ProfileDto can declare `Title? Title` and put
// "Mr" on the wire. Without it System.Text.Json writes an enum as its ORDINAL
// and the client receives 3. ProfileController.Summary works around that with
// .ToString() on every enum by hand; this fixes it once, for every endpoint.
// ===========================================================================
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// The name of the header the CSRF token may arrive in. ASP.NET would call it
// "RequestVerificationToken"; Angular's HttpClient sends "X-XSRF-TOKEN" without
// being asked, so this is the cheaper side to change. (Django's spelling is a
// third one again: "X-CSRFToken".)
//
// This does NOT disturb the .cshtml forms — [ValidateAntiForgeryToken] still
// reads the hidden __RequestVerificationToken field exactly as before. It only
// adds a second place the token is allowed to come from.
builder.Services.AddAntiforgery(options => options.HeaderName = "X-XSRF-TOKEN");

// ---------------------------------------------------------------------------
// Database
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' not found. Run: dotnet user-secrets set " +
        "\"ConnectionStrings:DefaultConnection\" \"Host=localhost;...;Database=remus_dotnet;...\"");

// ===========================================================================
// NOTE THE DIFFERENCE FROM THE BLAZOR PROJECT.
//
// There, ApplicationDbContext had to be handed out by an IDbContextFactory,
// because an interactive component's DI scope is the SignalR circuit — alive
// for hours — and one DbContext shared across overlapping renders is not
// thread-safe.
//
// Here, "scoped" means "one per HTTP request", full stop. A request is
// single-threaded and short-lived, so injecting ApplicationDbContext straight
// into a controller is correct and idiomatic. This is exactly Django's model:
// a request gets a connection, the request ends, the connection goes back.
//
// AddDbContext is the plain, boring, right answer in MVC. The factory dance
// only exists because Blazor Server broke the request/response assumption.
// ===========================================================================
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// Deliberately absent: AddDatabaseDeveloperPageExceptionFilter /
// UseMigrationsEndPoint. This project has no Data/Migrations folder and never
// calls Database.Migrate(). The Blazor app owns the schema; see the banner at
// the top of any file in Data/.

// ---------------------------------------------------------------------------
// Identity  —  Django: django.contrib.auth
// ---------------------------------------------------------------------------
// AddIdentity<TUser, TRole>, not AddIdentityCore<TUser>.
//
//   AddIdentityCore  = stores + managers only. You then wire the cookie
//                      authentication yourself. The Blazor template uses it.
//   AddIdentity      = all of that PLUS cookie authentication (the
//                      Application, External and TwoFactor schemes), plus
//                      RoleManager, plus the role-aware claims principal
//                      factory. The classic MVC choice.
//
// Because AddIdentity registers the cookie schemes for us, there is no
// AddAuthentication(...).AddIdentityCookies() call here — and no separate
// .AddRoles<IdentityRole>() either, since the TRole generic argument already
// did that. (In the Blazor project, forgetting .AddRoles() silently broke
// every [Authorize(Roles = ...)] check.)
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        // These MUST match the Blazor app: we share its user rows, and
        // SchemaVersion in particular decides whether Identity queries the
        // passkey columns. A mismatch is a runtime "column does not exist".
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;

        options.SignIn.RequireConfirmedAccount = true;   // Django: no core equivalent

        options.Password.RequiredLength = 8;             // Django: AUTH_PASSWORD_VALIDATORS
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;

        options.Lockout.MaxFailedAccessAttempts = 5;     // Django: nothing in core
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// ---------------------------------------------------------------------------
// The auth cookie  —  Django: SESSION_COOKIE_NAME, LOGIN_URL, SESSION_COOKIE_AGE
// ---------------------------------------------------------------------------
builder.Services.ConfigureApplicationCookie(options =>
{
    // A DISTINCT NAME MATTERS HERE.
    //
    // Cookies are scoped by host, NOT by port. Both apps run on localhost, so
    // without this the Blazor app's ".AspNetCore.Identity.Application" cookie
    // would be sent to this app too. It could not be decrypted — the two apps
    // have different Data Protection key rings — so it would be silently
    // discarded, but you would waste an afternoon working out why.
    options.Cookie.Name = ".Remus.Mvc.Auth";

    // Django: LOGIN_URL. Where [Authorize] sends an anonymous visitor.
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";

    // Django: no direct equivalent — @login_required always redirects to login,
    // whereas .NET distinguishes "not signed in" (401 -> LoginPath) from
    // "signed in but not allowed" (403 -> AccessDeniedPath).
    options.AccessDeniedPath = "/Account/AccessDenied";

    // Django: the "next" query parameter.
    options.ReturnUrlParameter = "returnUrl";

    options.ExpireTimeSpan = TimeSpan.FromDays(14);   // Django: SESSION_COOKIE_AGE
    options.SlidingExpiration = true;                 // Django: SESSION_SAVE_EVERY_REQUEST

    // =======================================================================
    // 401 FOR /api, 302 FOR EVERYTHING ELSE.
    //
    // This is the fix for the trap documented at length in
    // wwwroot/js/profile-edit.js. By default an expired cookie makes the
    // framework answer a REDIRECT to LoginPath — correct for a browser
    // following a link, useless for fetch(), which follows the redirect
    // silently and hands the caller 200 OK carrying an HTML login form where
    // it expected JSON.
    //
    // profile-edit.js defends by sniffing Content-Type. That was the honest
    // fix available from the client. This is the fix at the source: tell the
    // framework that /api talks to programs, and a program is owed a status
    // code rather than a page.
    //
    // Django hits this too, and DRF answers it the same way — session
    // authentication returns 403 instead of the 302 a browser would get,
    // precisely because an XHR cannot follow a login redirect usefully.
    // =======================================================================
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments(ApiSetup.Prefix))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };

    // The same distinction for "signed in, but not allowed here": 403 rather
    // than a redirect to AccessDeniedPath.
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments(ApiSetup.Prefix))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});

// ===========================================================================
// MANAGEMENT COMMANDS  —  Django: manage.py
//
// Django has two entry points over one configuration: wsgi.py serves, manage.py
// runs commands. .NET has one. So a command is a branch taken before app.Run(),
// after the container is built — which is the point, because it means commands
// get the very same DbContext, UserManager and password rules as the web app.
//
//     dotnet run -- createsuperuser
//
// The framework's own logging is chatty enough (every EF SQL statement at
// Information) to bury a command's output, so commands silence it entirely and
// print their own results — which is how manage.py behaves too.
//
// ClearProviders, not SetMinimumLevel: the "Logging" section of appsettings.json
// is already bound to the filter rules by this point, and those beat a minimum
// level set in code. Removing the providers is the unambiguous version.
// Nothing is lost — a real failure here throws, and an unhandled exception
// still prints its stack trace.
// ===========================================================================
var isCommand = args is [CreateSuperUser.Verb, ..];

if (isCommand)
    builder.Logging.ClearProviders();

var app = builder.Build();

// Returns an exit code and never starts the web server.
if (isCommand)
    return await CreateSuperUser.RunAsync(app.Services, args);


// ===========================================================================
// THE PIPELINE  —  Django's MIDDLEWARE list, in call order.
// ===========================================================================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    // Django: DEBUG = True's yellow traceback page.
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();

// Serves wwwroot. MapStaticAssets (below) handles build-time assets; this
// handles anything written at runtime. Django: STATIC + MEDIA.
app.UseStaticFiles();

app.UseRouting();

// ===========================================================================
// ORDER IS LOAD-BEARING, AND THE TEMPLATE DOES NOT WRITE THIS LINE FOR YOU.
//
// `dotnet new mvc --auth None` emits UseAuthorization() but NOT
// UseAuthentication(). Add Identity without adding this line and the symptom
// is brutal: every [Authorize] action redirects to the login page, you log in
// successfully, and you are redirected straight back to login — forever.
//
// Why: UseAuthentication is what reads the cookie and builds HttpContext.User.
// Without it User is always anonymous, so UseAuthorization always rejects.
//
// Django's MIDDLEWARE has the identical constraint — AuthenticationMiddleware
// must come after SessionMiddleware — but the ordering ships correct by
// default and you never touch it.
// ===========================================================================
app.UseAuthentication();   // reads the cookie  -> HttpContext.User
app.UseAuthorization();    // enforces [Authorize] against that User

// Issues the XSRF-TOKEN cookie on every GET under /api, giving the Angular
// client a token to echo back on its writes. This is the client half of what
// the hidden __RequestVerificationToken field does for the .cshtml forms; the
// server half is AntiforgeryFilter. Both are explained in Api/ApiSetup.cs.
app.UseApiAntiforgeryCookie();

app.MapStaticAssets();

// ---------------------------------------------------------------------------
// ROUTING  —  Django: urls.py
// ---------------------------------------------------------------------------
// ONE central route template, matched by convention:
//
//     /                      -> HomeController.Index()      (both defaulted)
//     /Profile               -> ProfileController.Index()   (action defaulted)
//     /Profile/Edit          -> ProfileController.Edit()
//     /Account/Login         -> AccountController.Login()
//
// This is the single biggest structural difference from the Blazor project,
// where every component declared its own route with @page "/profile/edit" and
// there was no central list at all.
//
// Versus Django: urls.py is explicit — every URL is written out and gets a
// name for {% url %}. Here the URLs are IMPLIED by class and method names, so
// renaming ProfileController silently changes your URLs. The trade is less
// typing for less visibility. (Attribute routing — [Route("profile/edit")] on
// the action — is the opt-in way back to Django-style explicitness.)
app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// ---------------------------------------------------------------------------
// THE API'S ROUTES  —  Django: path("api/", include("api.urls"))
// ---------------------------------------------------------------------------
// The counterpart to MapControllerRoute above, and the contrast IS the lesson.
// That one call routes every controller by convention, and you cannot tell
// from reading it which URLs exist. MapApi mounts a list of explicit route
// strings instead, so Api/ProfileEndpoints.cs reads like urls.py.
//
// Both styles are live in this one app, on one port, behind one auth cookie.
app.MapApi();

// What MapApi just registered, as a machine-readable document:
//     http://localhost:5260/openapi/v1.json
// Development only — an OpenAPI document is a map of your attack surface.
if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.Run();

// Required because the command branch above returns an int: once one path out
// of top-level statements yields an exit code, every path must.
return 0;
