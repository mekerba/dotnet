# Remus MVC

The same login and profile as [`remus_dotnet`](../remus_dotnet), rebuilt in
**pure ASP.NET Core MVC** — controllers, `.cshtml` views, tag helpers — against
**the same PostgreSQL database**.

Built to learn MVC by contrast: two apps, one schema, two architectures.
**[NOTES-MVC.md](NOTES-MVC.md)** is the write-up.

## Relationship to the Blazor app

```
              PostgreSQL: remus_dotnet
              AspNetUsers · Profiles · AspNetRoles
                   │                      │
     ┌─────────────┘                      └─────────────┐
     │                                                  │
  remus_dotnet  (Blazor)                       dotnet_mvc  (MVC)
  :5199                                        :5299
  OWNS the schema, runs migrations              reads/writes only
  Razor components, SignalR circuit             controllers + views
```

- The **Blazor app owns the schema.** This project has no `Data/Migrations`
  folder and never calls `Database.Migrate()`. Everything under `Data/` is a
  copy of its twin — see the banner at the top of each file.
- **The same account works in both**, but sessions are separate: different
  cookie names, different Data Protection keys.
- Avatars do **not** appear here. `Profile.ProfileImage` is a path into the
  *other* app's `wwwroot`. A shared database is not a shared filesystem.

## Requirements

- .NET SDK 10
- The `remus_dotnet` database, already migrated by the Blazor app

## Setup

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Port=5432;Database=remus_dotnet;Username=postgres;Password=..."
```

## Run

```bash
dotnet watch run --urls http://localhost:5299
```

Sign in with any account from the Blazor app (e.g. the seeded
`admin@remus.local`).

## Routes

| URL | Controller / action | Access |
|---|---|---|
| `/` | `Home.Index` | anonymous |
| `/Account/Login` | `Account.Login` (GET + POST) | anonymous |
| `/Account/Logout` | `Account.Logout` (POST only) | authenticated |
| `/Account/AccessDenied` | `Account.AccessDenied` | anonymous |
| `/Profile` | `Profile.Index` | `[Authorize]` |
| `/Profile/Edit` | `Profile.Edit` (GET + POST) | `[Authorize]` |

All of these come from one route template in `Program.cs`:
`{controller=Home}/{action=Index}/{id?}`.

## Layout

```
dotnet_mvc/
├─ Program.cs          DI + middleware + the single route template
├─ Controllers/        Django: views.py
│  ├─ HomeController.cs
│  ├─ AccountController.cs     hand-written: login, logout, access denied
│  └─ ProfileController.cs     [Authorize], EF injected directly
├─ Views/              Django: templates/
│  ├─ _ViewImports.cshtml      Django: {% load %}, but hierarchical
│  ├─ _ViewStart.cshtml        sets the layout for every view
│  ├─ Shared/_Layout.cshtml    Django: base.html
│  ├─ Shared/_ProfileCard.cshtml   a TYPED partial
│  ├─ Account/Login.cshtml
│  └─ Profile/{Index,Edit}.cshtml
├─ Models/             Django: forms.py  (view models, NOT models.py)
├─ Data/               Django: models.py — COPIED, schema owned elsewhere
└─ wwwroot/            Django: static/
```
