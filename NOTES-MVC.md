# MVC notes: Django → ASP.NET Core MVC → Blazor

Companion to [`../remus_dotnet/NOTES.md`](../remus_dotnet/NOTES.md), which
covers the Django↔.NET mapping in general. This file is about **MVC
specifically**, and about what changes when you rebuild the same feature as
components instead.

Three implementations of one thing are now available to compare:

| | Django (`remus_local`) | MVC (here) | Blazor (`remus_dotnet`) |
|---|---|---|---|
| Handler | `views.py` | `Controllers/*.cs` | the `@code` block |
| Template | `templates/*.html` | `Views/*.cshtml` | the same `.razor` file |
| Routing | `urls.py` | one route template | `@page` per component |
| Form | `forms.py` | `Models/*ViewModel.cs` | `Models/*FormModel.cs` |
| Data | `models.py` | `Data/` | `Data/` |

---

## 1. The vocabulary trap

This is the first thing to fix in your head, and it trips up everyone.

| Role | Django says | MVC says |
|---|---|---|
| Handles the request | **View** (`views.py`) | **Controller** |
| Renders the HTML | **Template** (`.html`) | **View** (`.cshtml`) |
| Holds the data | **Model** | **Model** |

**A Django View is an MVC Controller. An MVC View is a Django Template.**

So when a .NET tutorial says "put that in the view", it means the template. And
Django's "MVT" is not a different architecture from MVC — it is MVC with two
words swapped, plus the claim that the framework itself is the controller
(because `urls.py` dispatches for you).

---

## 2. Request lifecycle, side by side

**Django — `POST /profile/edit/`**

```
WSGI → SecurityMiddleware → SessionMiddleware → CsrfViewMiddleware
     → AuthenticationMiddleware → URLResolver → profile_edit(request)
     → ProfileForm(request.POST, instance=profile)
     → form.is_valid() → form.save() → redirect("profile") → 302
```

**MVC — `POST /Profile/Edit`**

```
Kestrel → UseHttpsRedirection → UseStaticFiles → UseRouting
        → UseAuthentication   (cookie -> HttpContext.User)
        → UseAuthorization    ([Authorize] on ProfileController)
        → model binding  (form fields -> ProfileEditViewModel)
        → validation     (DataAnnotations -> ModelState)
        → ProfileController.Edit(model)
        → ModelState.IsValid → SaveChangesAsync → RedirectToAction → 302
```

Same onion, two differences worth noticing:

1. **Binding and validation run *before* your method body.** Django constructs
   the form explicitly, so you can see it happen. In MVC `ModelState` is simply
   already populated when you arrive. Less visible, less to forget.
2. **Authorization is a pipeline stage, not a decorator.** `[Authorize]` is
   metadata the middleware reads; `@login_required` is a wrapper around the
   function. Which is why §3.1 below can bite.

---

## 3. Traps hit while building this

### 3.1 `[AllowAnonymous]` beats `[Authorize]`, no matter how far away

The first draft had `[AllowAnonymous]` on `AccountController` and `[Authorize]`
on the `Logout` action. The analyzer rejected it:

```
warning ASP0026: This [Authorize] attribute is overridden by an
[AllowAnonymous] attribute from farther away on 'AccountController'.
```

It is **not** "nearest wins" like CSS specificity. `[AllowAnonymous]` anywhere
in the chain wins, so the class-level attribute silently un-protected `Logout`.

Fix: no class-level attribute, `[AllowAnonymous]` on each public action.

Django cannot have this bug — `@login_required` decorates one function and
there is no enclosing scope that can cancel it.

### 3.2 `--auth None` omits `UseAuthentication()`

`dotnet new mvc --auth None` emits `app.UseAuthorization()` but **not**
`app.UseAuthentication()`. Add Identity and forget that line and the symptom is
brutal: every `[Authorize]` action redirects to login, you log in successfully,
and you land back on login — forever.

`UseAuthentication` is what reads the cookie and builds `HttpContext.User`.
Without it `User` is always anonymous, so `UseAuthorization` always rejects.

Django's `MIDDLEWARE` has the identical ordering constraint, but it ships
correct and you never touch it.

### 3.3 `--auth Individual` is not MVC

`dotnet new mvc --auth Individual` scaffolds Identity's **Default UI**, which
is a Razor Class Library of **Razor Pages**. You get `/Identity/Account/Login`
rendered by a `PageModel`, not a controller — so you would be learning Razor
Pages while believing you were learning MVC.

This project uses `--auth None` and hand-writes `AccountController`. Django
parallel: `include("django.contrib.auth.urls")` versus writing your own
`LoginView`.

### 3.4 Antiforgery is opt-in, not opt-out

The `<form asp-action>` tag helper injects the hidden token automatically, so
you cannot forget the `{% csrf_token %}` half. But **validating** it requires
`[ValidateAntiForgeryToken]` on the receiving action, per action.

Django is safe by default and you opt *out* with `@csrf_exempt`. MVC is the
reverse. `[AutoValidateAntiforgeryToken]` applied globally flips MVC to
Django's posture, and is what most production apps do.

*(Incidental discovery while testing: a page can carry several antiforgery
tokens — the nav's logout form emits one too. They are interchangeable, because
a token is bound to the request/user pair, not to a particular `<form>`.)*

### 3.5 `returnUrl` is an open redirect unless you check it

```
/Account/Login?returnUrl=https://evil.example/login
```

The victim sees your real domain, logs in for real, and is bounced to a
convincing fake. `Url.IsLocalUrl(returnUrl)` rejects anything with a scheme or
host — see `AccountController.RedirectToLocal`.

Django's `LoginView` calls `url_has_allowed_host_and_scheme()` on `next` for
you. In MVC it is your job, in every action that honours a return URL.

### 3.6 `Html.GetEnumSelectList<T>()` emits ordinals

The built-in helper renders `<option value="0">Mr</option>`. Model binding
accepts the ordinal, so it works — but the database stores `'Mr'`, because
`ProfileConfiguration` maps the enum with `.HasConversion<string>()`. Form and
column then disagree, and reordering the enum would silently repoint any
cached page.

`Views/Profile/Edit.cshtml` builds the list from `Enum.GetValues<T>()` with
names on both sides. Django's `TextChoices` gives you this by default.

### 3.7 Cookies are scoped by host, not by port

Both apps run on `localhost`, so the Blazor app's
`.AspNetCore.Identity.Application` cookie would be sent to this app too. It
could not be decrypted — different Data Protection key rings — so it would be
discarded silently, and you would lose an afternoon.

Fixed by naming this app's cookie `.Remus.Mvc.Auth` in
`ConfigureApplicationCookie`.

### 3.8 A shared database is not a shared filesystem

`Profile.ProfileImage` holds `/uploads/avatars/….png`, a path into the *Blazor*
app's `wwwroot`. Rendering it here 404s. `_ProfileCard.cshtml` shows initials
instead.

This is exactly why Django keeps `MEDIA_ROOT` out of the database, and why any
real deployment puts uploads in object storage behind a URL every app can
reach.

---

## 4. MVC vs Blazor, same feature

| | MVC (here) | Blazor (`remus_dotnet`) |
|---|---|---|
| Registration | `AddControllersWithViews()` | `AddRazorComponents().AddInteractiveServerComponents()` |
| Identity | `AddIdentity<TUser, TRole>()` — cookies + roles included | `AddIdentityCore<T>()` + `AddAuthentication().AddIdentityCookies()` + `.AddRoles<>()` |
| Routing | one central `MapControllerRoute` | `@page` on each component |
| DbContext | `AddDbContext` — **scoped is correct** | `AddDbContextFactory` **required** |
| Why | a controller is per-request, single-threaded | a circuit lives for hours; `DbContext` is not thread-safe |
| Form submit | real HTTP POST, full page response | a message over SignalR, DOM diff pushed back |
| Validation errors | server re-renders the whole page | server patches the DOM |
| Flash message | `TempData` (survives one redirect) | hand-rolled `StatusMessage.razor` |
| Partial | `<partial name="_X" model="…" />` | `<ProfileCard Profile="…" />` |
| Auth state in markup | `@if (User.Identity?.IsAuthenticated == true)` | `<AuthorizeView>` |
| Works with JS off | **yes, entirely** | no — interactive pages need the circuit |

The last row is the honest summary. **MVC is Django's execution model**: a
request arrives, a handler runs, HTML goes back, the server forgets you. Blazor
Server keeps a stateful connection per open tab, which buys live interactivity
and costs you the `DbContext` lifetime problem, the reconnect UI, and the rule
that cookies can only be set from static-SSR pages.

---

## 5. Tag helpers ↔ Django template tags

| Razor | Django |
|---|---|
| `<a asp-controller="Profile" asp-action="Edit">` | `{% url 'profile-edit' %}` |
| `<form asp-action="Login" method="post">` | `<form method="post">{% csrf_token %}` |
| `<label asp-for="Email">` | `{{ form.email.label_tag }}` |
| `<input asp-for="Email">` | `{{ form.email }}` |
| `<span asp-validation-for="Email">` | `{{ form.email.errors }}` |
| `<div asp-validation-summary="ModelOnly">` | `{{ form.non_field_errors }}` |
| `<select asp-for="X" asp-items="opts">` | a `ChoiceField` widget |
| `<partial name="_X" model="m" />` | `{% include "_x.html" with … %}` |
| `asp-append-version="true"` | `ManifestStaticFilesStorage` |
| `@RenderBody()` | `{% block content %}` |
| `@await RenderSectionAsync("Scripts", false)` | `{% block extra_js %}` |
| `@model X` | **no equivalent** — templates are untyped |
| `@Html.Raw(x)` | `{{ x|safe }}` |

`asp-for` is the one to internalise. From a single property it derives the
label text, `name`, `id`, `value`, the input `type` (from `[DataType]` /
`[EmailAddress]`) and every `data-val-*` client-validation attribute. Verified
output for `LoginViewModel.Email`:

```html
<input type="email" id="Email" name="Email"
       data-val="true"
       data-val-required="Enter your email address."
       data-val-email="That does not look like an email address." />
```

Django's `{{ form.email }}` does the same job from the form field definition.
The difference is that Razor checks the property exists at **compile** time.

---

## 6. Verified behaviours

Everything below was exercised over HTTP against the real database:

| | Result |
|---|---|
| `GET /Profile` anonymous | 302 → `/Account/Login?returnUrl=%2FProfile` |
| `POST /Account/Login` (account made in the Blazor app) | 302 → `/Profile`, `.Remus.Mvc.Auth` issued |
| `POST /Profile/Edit` with `Country=DZA` | **200**, re-rendered, `value="DZA"` preserved, message shown, **database untouched** |
| `POST /Profile/Edit` valid | 302 → `/Profile` (PRG) |
| `"  Mohamed  "` → column | `Mohamed` (trimmed by `ApplyTo`) |
| `dz` → column | `DZ` (upper-cased) |
| `Title=Dr` → column | `'Dr'`, not `3` (`HasConversion<string>()`) |
| `LastUpdate` after save | `> Created` — the `SaveChangesAsync` override fired |
| Flash message | rendered once, gone on reload (cookie-backed `TempData`) |

---

## 7. Commands

| Task | Django | Here |
|---|---|---|
| Run | `manage.py runserver` | `dotnet watch run --urls http://localhost:5299` |
| Migrations | `manage.py migrate` | **not here** — the Blazor app owns the schema |
| Secret | `.env` | `dotnet user-secrets set "K" "v"` |
| DB shell | `manage.py dbshell` | `psql -U postgres -d remus_dotnet` |
