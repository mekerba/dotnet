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

    /// <summary>Query root for events. Django: Event.objects</summary>
    public DbSet<Event> Events => Set<Event>();

    /// <summary>
    /// Query root for the enrolment rows. Django: EventParticipant.objects
    /// </summary>
    /// <remarks>
    /// A through model gets a manager in Django whether you want one or not,
    /// and here a DbSet is equally optional: EventParticipant is reachable by
    /// navigation from both Event and Profile, so EF would map it regardless.
    /// It is declared because the enrolment endpoints query it directly -
    /// "does this profile already have a row for this event" is a question
    /// about the join, not about either end of it.
    /// </remarks>
    public DbSet<EventParticipant> EventParticipants => Set<EventParticipant>();

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
    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        StampAuditedEntities();
        await AssignEventCodesAsync(cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampAuditedEntities();
        AssignEventCodes();
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

    // =======================================================================
    // Event.code  —  Django's save() override, relocated.
    //
    // The Django model assigns its own code:
    //
    //     def save(self, *args, **kwargs):
    //         if can_set_code and not self.code and self.start_date and self.type_id:
    //             for _attempt in range(5):
    //                 self.code = self._build_code()      # queries Event.objects
    //                 try:
    //                     with transaction.atomic():
    //                         return super().save(*args, **kwargs)
    //                 except IntegrityError:
    //                     self.code = None
    //
    // It can do that because a Django model reaches its own manager, so
    // _build_code() runs a query from inside the instance being saved. An EF
    // entity has no such reach and should not be given one - so the rule lives
    // here, in the one place that already intercepts every write, next to the
    // auto_now stamping it is a sibling of.
    //
    // THREE DIFFERENCES FROM THE DJANGO VERSION, all deliberate:
    //
    //  * IT IS SET-BASED. Django saves one instance at a time, so it needs one
    //    query per event. This runs once per (type, start-date) GROUP however
    //    many events are being inserted, because SaveChanges sees the whole
    //    batch at once. That is the unit-of-work paying for itself.
    //
    //  * THERE IS NO RETRY LOOP. Django re-queries and re-saves up to five
    //    times on an IntegrityError, because two concurrent creates can pick
    //    the same counter. Here the unique index on Code is the answer: the
    //    losing insert throws DbUpdateException and the caller decides. Burying
    //    a retry inside SaveChanges would mean silently re-running the caller's
    //    entire unit of work, which is a much bigger thing to do behind their
    //    back than Django's per-instance retry was.
    //
    //  * NOTHING IS RE-DERIVED. Both versions assign once and never touch an
    //    existing code, so editing StartDate or Type after the fact does not
    //    churn it. Django enforces that with `not self.code`; so does this.
    // =======================================================================

    /// <summary>
    /// New events that still need a code, grouped by the TYPE-YYYYMMDD prefix
    /// they will share.
    /// </summary>
    /// <remarks>
    /// Added only. An event already in the database keeps the code it was born
    /// with, even if someone has since moved its start date.
    /// </remarks>
    private List<IGrouping<string, Event>> PendingCodeGroups() =>
        ChangeTracker.Entries<Event>()
            .Where(e => e.State == EntityState.Added && string.IsNullOrEmpty(e.Entity.Code))
            .Select(e => e.Entity)
            .GroupBy(e => e.CodeBase)
            .ToList();

    private async Task AssignEventCodesAsync(CancellationToken ct)
    {
        foreach (var group in PendingCodeGroups())
        {
            // The codes this prefix has already handed out. StartsWith on an
            // indexed column translates to a Postgres LIKE 'TRN-20260610-%',
            // which the btree index on Code can serve.
            var taken = await Events
                .Where(e => e.Code != null && e.Code.StartsWith(group.Key))
                .Select(e => e.Code!)
                .ToListAsync(ct);

            AssignWithin(group, taken);
        }
    }

    /// <summary>
    /// The synchronous twin, for callers that used SaveChanges.
    /// </summary>
    /// <remarks>
    /// Duplicated rather than bridged with .GetAwaiter().GetResult(): blocking
    /// on an async database call is how you deadlock a thread pool, and the
    /// duplication is four lines. The shared part - which counter to pick - is
    /// in AssignWithin, so the rule itself is written once.
    /// </remarks>
    private void AssignEventCodes()
    {
        foreach (var group in PendingCodeGroups())
        {
            var taken = Events
                .Where(e => e.Code != null && e.Code.StartsWith(group.Key))
                .Select(e => e.Code!)
                .ToList();

            AssignWithin(group, taken);
        }
    }

    /// <summary>
    /// Hands each event in one prefix group the lowest counter still free.
    /// </summary>
    /// <remarks>
    /// `taken` grows as it goes, which is the part a per-instance save cannot
    /// do: without it, three events created in one batch on the same day and
    /// of the same type would all be handed -01 and two of them would lose to
    /// the unique index.
    /// </remarks>
    private static void AssignWithin(IEnumerable<Event> group, List<string> taken)
    {
        foreach (var ev in group)
        {
            ev.Code = ev.BuildCode(taken);
            taken.Add(ev.Code);
        }
    }
}
