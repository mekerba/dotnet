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

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Remus.Mvc.Data;

/// <summary>
/// The application's Entity Framework context.
/// </summary>
/// <remarks>
/// There is no single Django equivalent. A DbContext is three things Django
/// keeps apart:
///
///   1. The set of models the ORM knows about  -> Django's app registry
///      (INSTALLED_APPS + each app's models.py, discovered automatically).
///      Here it is explicit: a DbSet property, or reachable by navigation from
///      one. Nothing is auto-discovered.
///
///   2. A query entry point                    -> Django's Model.objects
///      Django hangs a manager off every model class; EF hangs every model off
///      one context. db.Profiles.Where(...) instead of Profile.objects.filter().
///
///   3. A unit of work                         -> Django has no equivalent
///      The context tracks every entity it loaded, notices what you changed,
///      and writes all of it in one transaction on SaveChangesAsync. Django
///      saves per-instance with .save(); there is no "pending changes" set.
///      This is why you will see code here mutate an object and then call
///      SaveChangesAsync with no argument - the context already knows.
///
/// Inheriting IdentityDbContext&lt;ApplicationUser&gt; brings in the seven AspNet*
/// entities (users, roles, claims, logins, tokens, user-roles, passkeys). It is
/// the moment ApplicationUser becomes "the" user type, and the closest analogue
/// to AUTH_USER_MODEL in settings.py.
/// </remarks>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    /// <summary>
    /// Query root for profiles. Django: Profile.objects
    /// </summary>
    /// <remarks>
    /// Expression-bodied (=> Set&lt;Profile&gt;()) rather than an auto-property with
    /// a setter. Both work; this form cannot be accidentally reassigned and
    /// needs no null-forgiving operator.
    /// </remarks>
    public DbSet<Profile> Profiles => Set<Profile>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Must come first: this is what creates the AspNet* entity mappings.
        // Skip it and Identity breaks in confusing ways.
        base.OnModelCreating(builder);

        // Scans this assembly for IEntityTypeConfiguration<T> implementations
        // and applies them - currently ProfileConfiguration. This is the opt-in
        // stand-in for Django discovering every models.py in INSTALLED_APPS.
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }

    // =======================================================================
    // auto_now_add / auto_now
    //
    // Django declares these on the field and the ORM stamps them on save.
    // EF Core has no such hook, so we override the save path once and stamp
    // everything implementing IAuditedEntity. Both SaveChanges overloads are
    // overridden because EF routes through whichever the caller used, and
    // missing the sync one leaves a silent gap.
    //
    // This is also the mechanism you would use to replace Django signals in
    // general (pre_save/post_save): an override here, or an
    // ISaveChangesInterceptor registered on the context. We deliberately do NOT
    // create Profile rows from here - that happens explicitly in
    // UserRegistrationService, for reasons noted there.
    // =======================================================================
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        StampAuditedEntities();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampAuditedEntities();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void StampAuditedEntities()
    {
        var now = DateTimeOffset.UtcNow;

        // ChangeTracker.Entries<T>() walks only entities the context is already
        // tracking, so this is cheap - it never queries the database.
        foreach (var entry in ChangeTracker.Entries<IAuditedEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.Created = now;
                    entry.Entity.LastUpdate = now;
                    break;

                case EntityState.Modified:
                    entry.Entity.LastUpdate = now;

                    // Belt and braces: mark Created as unmodified so a stale or
                    // hand-set value on a detached entity cannot overwrite the
                    // original. Django gets this for free because auto_now_add
                    // only fires on insert.
                    entry.Property(nameof(IAuditedEntity.Created)).IsModified = false;
                    break;
            }
        }
    }
}
