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
/// Maps <see cref="EventParticipant"/> - the join row - to the database.
/// </summary>
/// <remarks>
/// The two foreign keys are NOT configured here. They are set up by the
/// .UsingEntity call in EventConfiguration, because that is what makes them
/// the same relationship as the skip navigations rather than a second one
/// pointing at the same table. Configure them in both places and EF complains
/// about a duplicate relationship; configure them only here and you silently
/// get an extra join table. This file owns everything else.
/// </remarks>
public class EventParticipantConfiguration : IEntityTypeConfiguration<EventParticipant>
{
    public void Configure(EntityTypeBuilder<EventParticipant> builder)
    {
        builder.ToTable("EventParticipants");

        // ===================================================================
        // Django: class Meta: unique_together = ('event', 'participant')
        //
        // One row per person per event. This is the only thing stopping a
        // double-click on Enrol from producing two applications, and it is
        // deliberately the DATABASE that stops it rather than the endpoint.
        //
        // EventEndpoints does check for an existing enrolment first and
        // answers 409 - that check is for the error message, not for
        // correctness. Two requests arriving together both see "no enrolment",
        // both insert, and the index is what makes the second one fail. A
        // check-then-act in application code cannot close that window; a
        // unique index closes it by construction.
        // ===================================================================
        builder.HasIndex(ep => new { ep.EventId, ep.ProfileId })
               .IsUnique()
               .HasDatabaseName("ix_event_participants_event_profile");

        // Backs "everything this profile signed up for", which is the query
        // behind the enrolment badge on the events table.
        builder.HasIndex(ep => ep.ProfileId)
               .HasDatabaseName("ix_event_participants_profile");

        builder.Property(ep => ep.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(ep => ep.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(ep => ep.Mode).HasConversion<string>().HasMaxLength(20);

        builder.Property(ep => ep.Comments).HasMaxLength(5000);
    }
}
