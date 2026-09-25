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

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Remus.Mvc.Data.Configurations;

/// <summary>
/// Maps <see cref="Event"/> to the database.
/// </summary>
/// <remarks>
/// Django's field kwargs plus <c>class Meta</c>, in the same place
/// ProfileConfiguration puts them. Discovered automatically by
/// ApplyConfigurationsFromAssembly - see ApplicationDbContext.
/// </remarks>
public class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("Events");

        // ===================================================================
        // THE MANY-TO-MANY. This is the heart of the file, the way the
        // one-to-one is the heart of ProfileConfiguration.
        //
        // Django:
        //     participants = models.ManyToManyField(
        //         "users.Profile", through="EventParticipant", blank=True)
        //
        // One line, and it gives you event.participants (the profiles),
        // eventparticipant_set (the rows) and the knowledge that they are the
        // same relationship. EF wants that spelled out:
        //
        //   HasMany(e => e.Participants)      the skip navigation on this side
        //   WithMany(p => p.Events)           ...and its opposite on Profile
        //   UsingEntity<EventParticipant>(    the through model
        //       r => r.HasOne(...).WithMany(...)   how the join reaches Profile
        //       l => l.HasOne(...).WithMany(...))  how it reaches Event
        //
        // The two lambdas are named `r` and `l` for right and left, and getting
        // them the wrong way round compiles perfectly and then fails at model
        // build time with a message about an unmapped navigation. Read them as
        // "from the join row, to the far side": the first goes to Profile
        // because Profile is the OTHER entity in HasMany/WithMany.
        //
        // WHAT THIS BUYS: the skip navigations and the payload navigations end
        // up as one relationship rather than two that happen to share a table.
        // Without UsingEntity, EF would invent its own join table ("EventProfile")
        // alongside EventParticipants and you would have both, silently.
        // ===================================================================
        builder.HasMany(e => e.Participants)
               .WithMany(p => p.Events)
               .UsingEntity<EventParticipant>(
                   r => r.HasOne(ep => ep.Profile)
                         .WithMany(p => p.Participations)
                         .HasForeignKey(ep => ep.ProfileId)
                         .OnDelete(DeleteBehavior.Cascade),
                   l => l.HasOne(ep => ep.Event)
                         .WithMany(e => e.Participations)
                         .HasForeignKey(ep => ep.EventId)
                         .OnDelete(DeleteBehavior.Cascade));

        // ---- lengths -------------------------------------------------------
        // Django: CharField(max_length=500). EF: varchar(500). Without
        // HasMaxLength, Npgsql maps string to unbounded `text` - see the note
        // in ProfileConfiguration.
        builder.Property(e => e.Title).HasMaxLength(500).IsRequired();
        builder.Property(e => e.ShortTitle).HasMaxLength(500);
        builder.Property(e => e.Objectives).HasMaxLength(2000);
        builder.Property(e => e.City).HasMaxLength(200);
        builder.Property(e => e.Venue).HasMaxLength(200);
        builder.Property(e => e.HostInstitution).HasMaxLength(200);

        // ISO codes, same treatment as Profile.Country / Profile.Nationality.
        builder.Property(e => e.HostCountry).HasMaxLength(2);
        builder.Property(e => e.Language).HasMaxLength(2);

        // ---- the code ------------------------------------------------------
        // Django: unique=True, db_index=True, editable=False, max_length=20.
        //
        // UNIQUE IS THE POINT. ApplicationDbContext assigns this value by
        // querying for the lowest free counter, and two concurrent inserts can
        // pick the same one - the query cannot see an uncommitted row. Django
        // guards that with a retry loop around IntegrityError. Here the index
        // is the guard: the second insert fails with a DbUpdateException rather
        // than quietly producing two events called TRN-20260610-01. Losing the
        // insert is the correct failure; sharing a code is not.
        builder.Property(e => e.Code).HasMaxLength(20);
        builder.HasIndex(e => e.Code)
               .IsUnique()
               .HasDatabaseName("ix_events_code");

        // ---- enums as text --------------------------------------------------
        // .HasConversion<string>() on every one, for the reasons in
        // ProfileEnums.cs: readable in psql, and stable when someone reorders
        // the enum. This is what Django's TextChoices does for free.
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Level).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Nature).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.AttendanceNature).HasConversion<string>().HasMaxLength(20);

        // ---- constraints -----------------------------------------------------
        // Django: validators=[MinValueValidator(0)], which runs in full_clean()
        // and therefore only when a form is involved. This is the database
        // saying no, whatever the caller is.
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_events_seats_limit_non_negative", "\"SeatsLimit\" >= 0"));

        // Not expressible in Django's validators at all (it spans two fields,
        // so it would be a clean() method on the model). A CHECK constraint
        // costs nothing and rules out the whole class of "event ends before it
        // starts" bugs at the only layer that sees every write.
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_events_dates_ordered", "\"EndDate\" >= \"StartDate\""));

        // ---- indexes ----------------------------------------------------------
        // Django: class Meta: ordering = ['-start_date'] - which is an ORDER BY
        // on every query whether you wanted one or not. EF has no equivalent
        // and does not want one: ordering is a property of a query, not of a
        // table, so EventEndpoints says .OrderByDescending(e => e.StartDate)
        // explicitly. This index is what makes that cheap.
        builder.HasIndex(e => e.StartDate)
               .HasDatabaseName("ix_events_start_date");

        // Backs the default listing filter: upcoming, approved events first.
        builder.HasIndex(e => new { e.Status, e.StartDate })
               .HasDatabaseName("ix_events_status_start_date");
    }
}
