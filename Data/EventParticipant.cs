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

namespace Remus.Mvc.Data;

/// <summary>
/// One profile's enrolment in one event, with the workflow that goes with it.
/// </summary>
/// <remarks>
/// Django equivalent: events/models.py's EventParticipant, the <c>through=</c>
/// model of <c>Event.participants</c>.
///
/// ---------------------------------------------------------------------------
/// WHY THIS IS A CLASS AND NOT A CONFIGURATION LINE.
///
/// A many-to-many with NO extra columns needs no entity in EF Core at all -
/// two ICollection properties and EF invents the join table, the same way
/// Django does for a plain ManyToManyField. The moment there is a payload,
/// both frameworks make you name the middle: Django with <c>through=</c>, EF
/// with a class and <c>.UsingEntity&lt;T&gt;()</c>.
///
/// And there is a lot of payload. Everything below the two foreign keys is the
/// reason this relationship could never have been implicit: a role, a status,
/// three workflow dates, a check-in flag, a visa flag. This is not a link
/// table, it is an application form that happens to point at two rows.
/// ---------------------------------------------------------------------------
///
/// AN EXPLICIT Id, WHERE DJANGO ALSO HAS ONE.
///
/// Django's through model gets the usual implicit BigAutoField pk, plus
/// <c>unique_together = ('event', 'participant')</c> to stop duplicates. EF
/// would default to a composite key of (EventId, ProfileId) when configured
/// through .UsingEntity, which is arguably the better schema - but it makes
/// the row unaddressable by a single value, so DELETE /api/events/{id}/
/// enrolment would need both halves in the URL. The surrogate key plus a
/// unique index is the same shape as Django's and keeps the API simple.
/// </remarks>
public class EventParticipant : IAuditedEntity
{
    public int Id { get; set; }

    // ---- the two ends ---------------------------------------------------------
    // Django: event = models.ForeignKey(Event, on_delete=models.CASCADE)
    //         participant = models.ForeignKey(Profile, on_delete=models.CASCADE)
    //
    // Same three-part expansion as Profile.User: the FK column, the navigation
    // property, and the mapping that gives them meaning. CASCADE on both sides
    // is configured in EventParticipantConfiguration - deleting an event takes
    // its enrolments with it, and so does deleting a profile.
    public int EventId { get; set; }
    public Event Event { get; set; } = null!;

    // Named Profile, not Participant. Django's field is `participant` and its
    // reverse accessor is `eventparticipant_set`; here the navigation is named
    // after the TYPE it points at, which is the EF convention and what makes
    // .Include(p => p.Profile) readable.
    public int ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;

    // ---- the application form --------------------------------------------------
    public ParticipantRole Role { get; set; } = ParticipantRole.Participant;

    /// <summary>Where this enrolment sits in the workflow.</summary>
    /// <remarks>
    /// NOT settable through the API. An applicant POSTing to
    /// /api/events/{id}/enrolment always lands on Applied; moving to Selected
    /// or Accepted is an operator's decision and there is no endpoint for it
    /// yet. See the over-posting note on ProfileUpdateDto - a status column is
    /// the textbook case, because "accept my own application" is one crafted
    /// JSON field away if you ever bind this from a request.
    /// </remarks>
    public ParticipationStatus Status { get; set; } = ParticipationStatus.Applied;

    /// <summary>
    /// Asked only for Hybrid / HybridStreaming events; NULL everywhere else.
    /// </summary>
    public ParticipationMode? Mode { get; set; }

    // ---- the workflow dates ----------------------------------------------------
    // One per status transition, which is a denormalised way of storing the
    // history that Django's EventParticipantLog stores properly. Both exist in
    // remus_local; only these are ported, because the log table is an operator
    // audit trail and no screen here reads it.
    public DateOnly? ApplicationDate { get; set; }
    public DateOnly? SelectionDate { get; set; }
    public DateOnly? AcceptanceDate { get; set; }

    // ---- on the day -------------------------------------------------------------
    public bool CheckedIn { get; set; }
    public DateOnly? CheckinDate { get; set; }

    /// <summary>Django: need_visa, asked at registration.</summary>
    public bool NeedVisa { get; set; }

    public string? Comments { get; set; }

    // ---- audit --------------------------------------------------------------------
    // Django's EventParticipant has no created/last_update pair at all - the
    // three workflow dates stand in for it. Adding them costs nothing (the
    // DbContext stamps anything implementing IAuditedEntity) and answers
    // "when did this person actually sign up", which ApplicationDate only
    // approximates because an operator can back-date it.
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset LastUpdate { get; set; }
}
