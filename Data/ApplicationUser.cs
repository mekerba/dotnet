// ============================================================================
// COPIED FROM ../remus_dotnet/src/Remus.Web/Data/ - DO NOT EDIT IN ISOLATION.
//
// This project SHARES the `remus_dotnet` PostgreSQL database with the Blazor
// app. That app owns the schema and the migration history; this one only reads
// and writes. There is deliberately no Data/Migrations folder here and nothing
// ever calls Database.Migrate().
//
// Consequence: this file must stay byte-for-byte equivalent (namespace aside)
// to its twin. If EF's model drifts from the real tables, queries fail at
// runtime with "column does not exist" rather than at build time.
//
// Django analogue: two Django projects pointed at one database, where only one
// has the migrations and the other runs with `managed = False` models.
// ============================================================================

using Microsoft.AspNetCore.Identity;

namespace Remus.Mvc.Data;

/// <summary>
/// The application's user entity - the "CustomUser".
/// </summary>
/// <remarks>
/// Django equivalent:
///
///     class CustomUser(AbstractBaseUser, PermissionsMixin):
///         ...
///     # settings.py
///     AUTH_USER_MODEL = "users.CustomUser"
///
/// Differences worth internalising:
///
/// * The template already did the swap. In Django, forgetting to set
///   AUTH_USER_MODEL before the first migrate is a well-known trap that costs
///   you a database reset. Here `dotnet new blazor -au Individual` generated
///   this class and wired IdentityDbContext&lt;ApplicationUser&gt; to it up front,
///   so the trap is pre-defused. The equivalent trap that remains is changing
///   the *key type* (string -> Guid/int) after migrating.
///
/// * IdentityUser gives you: Id, UserName, NormalizedUserName, Email,
///   NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp,
///   ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled,
///   LockoutEnd, LockoutEnabled, AccessFailedCount.
///
///   Django's AbstractUser gives you: username, first_name, last_name, email,
///   is_staff, is_active, is_superuser, date_joined, last_login, password.
///
///   Note what each framework thinks belongs on a user. Identity has no names
///   (that is what Profile is for) but ships lockout counters, 2FA and a
///   security stamp. Django has names and permission flags but no lockout or
///   2FA in core. That difference is the whole reason remus_local needed six
///   boolean role columns and a separate profile table.
///
/// * NormalizedEmail is Identity's answer to case-insensitive lookup: it stores
///   an upper-cased copy and puts a unique index on it. remus_local solves the
///   same problem with a functional index, users_cuser_lower_email_idx on
///   lower(email). Same goal, opposite technique - a denormalised column that
///   any database can index, versus an expression index that Postgres can.
/// </remarks>
public class ApplicationUser : IdentityUser
{
    /// <summary>
    /// The user's profile. One per user, created during registration.
    /// </summary>
    /// <remarks>
    /// This is the inverse side of the one-to-one. Django would give you this
    /// for free from the OneToOneField's related_name="profile", and would
    /// lazily hit the database on first access (raising RelatedObjectDoesNotExist
    /// if absent).
    ///
    /// EF Core does neither. This property is null until something loads it -
    /// .Include(u => u.Profile), an explicit Load(), or a projection. Nullable
    /// (Profile?) because "not loaded" and "does not exist" are both real states
    /// and the type system should admit it.
    ///
    /// UserRegistrationService guarantees a profile row exists for every user,
    /// so a null here at runtime means "you forgot to Include it", not "this
    /// user has no profile".
    /// </remarks>
    public Profile? Profile { get; set; }
}
