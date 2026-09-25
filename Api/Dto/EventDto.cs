using Remus.Mvc.Data;

namespace Remus.Mvc.Api.Dto;

// ============================================================================
// The read side of the events API. DRF's serializers.py, output half.
//
// TWO SHAPES FOR ONE ENTITY, and the split is not ceremony.
//
// EventListItemDto is what a table row needs. EventDetailDto is that plus the
// prose. The difference is Objectives, a varchar(2000) that no row in a list
// of forty events will ever show - sending it forty times is most of the
// payload for none of the pixels.
//
// Django's equivalents are .only() and .values(), and they have the same
// payoff and the same trap: the moment a caller touches a field that was not
// selected, Django silently issues another query per row, and you have an N+1
// that profiles fine on a dataset of three. EF does not do that. A projection
// returns a DTO, not a lazy entity, so a field that was not selected is not
// there - it does not compile. That is the whole argument for projecting into
// a DTO rather than fetching entities and mapping afterwards.
// ============================================================================

/// <summary>One row of GET /api/events.</summary>
/// <remarks>
/// Built by a .Select() inside the query, never by loading an Event and
/// copying it - see the note on EventEndpoints.List. Which is why this type
/// has no FromEntity method, unlike ProfileDto: there is no entity to convert.
/// The SELECT statement names these columns and no others.
/// </remarks>
public sealed record EventListItemDto
{
    public required int Id { get; init; }

    /// <summary>e.g. <c>TRN-20260610-01</c>. Null only for a row mid-insert.</summary>
    public string? Code { get; init; }

    public required string Title { get; init; }
    public string? ShortTitle { get; init; }

    // Enums go out as "Training", not 3, because Program.cs registers a
    // JsonStringEnumConverter. Without it every string-literal union in the
    // Angular client would be a lie - see api.models.ts.
    public required EventType Type { get; init; }
    public required EventStatus Status { get; init; }
    public required EventLevel Level { get; init; }
    public required EventNature Nature { get; init; }
    public required AttendanceNature AttendanceNature { get; init; }

    public string? HostCountry { get; init; }
    public string? City { get; init; }
    public string? Venue { get; init; }
    public string? Language { get; init; }

    // DateOnly serialises as "2026-06-10", which is exactly what
    // <input type="date"> and the Angular DatePipe both want. Nothing parses.
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }

    /// <summary>0 means unlimited.</summary>
    public required int SeatsLimit { get; init; }

    /// <summary>How many enrolments exist, whatever their status.</summary>
    public required int ParticipantCount { get; init; }

    // ---- computed, and computed HERE on purpose -----------------------------
    // Each of these is one line the Angular app would otherwise have to write
    // for itself, from fields it would otherwise have to be sent. Putting the
    // rule on the server means there is one definition of "full" rather than
    // two that agree until someone edits one.
    //
    // Event.IsExpired is the same property on the entity, recomputed here
    // because this DTO never sees the entity - it is projected straight out of
    // SQL. The duplication is real and is the price of projecting. The
    // alternative (load entities, read the property, map) costs a wider SELECT
    // on every row.

    /// <summary>True once EndDate has passed, whatever Status says.</summary>
    public bool IsExpired => EndDate < DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Null when SeatsLimit is 0, i.e. unlimited.</summary>
    public int? SeatsRemaining =>
        SeatsLimit == 0 ? null : Math.Max(0, SeatsLimit - ParticipantCount);

    public bool IsFull => SeatsRemaining == 0;

    /// <summary>
    /// Whether this event is accepting applications at all.
    /// </summary>
    /// <remarks>
    /// The client half of the rule that EventEndpoints.Enrol enforces. Both
    /// halves exist and they are NOT redundant: this one greys out a button,
    /// that one refuses the write. A rule that lives only here is decoration -
    /// the request can always be sent by hand.
    /// </remarks>
    public bool IsOpenForEnrolment =>
        Status == EventStatus.Approved && !IsExpired && !IsFull;

    /// <summary>
    /// The caller's own enrolment, or null. Always null for an anonymous caller.
    /// </summary>
    /// <remarks>
    /// This is the field that makes GET /api/events answer differently for two
    /// callers, and it is the API equivalent of a .cshtml writing
    /// <c>@if (User.Identity.IsAuthenticated)</c> around a badge. It is on the
    /// LIST rather than fetched separately so the table can render "Applied"
    /// per row without a request per row.
    /// </remarks>
    public MyEnrolmentDto? MyEnrolment { get; init; }
}

/// <summary>GET /api/events/{id} — the list row plus the prose.</summary>
public sealed record EventDetailDto
{
    public required EventListItemDto Summary { get; init; }

    public string? Objectives { get; init; }
    public string? HostInstitution { get; init; }

    public required DateTimeOffset Created { get; init; }
    public required DateTimeOffset LastUpdate { get; init; }
}

/// <summary>
/// The caller's own enrolment, as it appears next to an event.
/// </summary>
/// <remarks>
/// Deliberately thinner than EventParticipantDto. That one describes SOMEBODY
/// ELSE to you and is trimmed for privacy; this one describes YOU to you, so
/// it carries the workflow fields (which date you applied, whether you are
/// accepted) that would be nobody's business on another person's row.
/// </remarks>
public sealed record MyEnrolmentDto
{
    public required int Id { get; init; }
    public required ParticipationStatus Status { get; init; }
    public required ParticipantRole Role { get; init; }
    public ParticipationMode? Mode { get; init; }
    public DateOnly? ApplicationDate { get; init; }
    public required bool CheckedIn { get; init; }
    public required bool NeedVisa { get; init; }

    /// <summary>
    /// Whether the caller may still withdraw this themselves.
    /// </summary>
    /// <remarks>
    /// Mirrors the rule in EventEndpoints.Withdraw: once an operator has moved
    /// you past Applied, un-applying is their decision and not yours.
    /// </remarks>
    public bool CanWithdraw => Status == ParticipationStatus.Applied;
}

/// <summary>
/// One row of GET /api/events/{id}/participants — somebody else, seen by a
/// fellow participant.
/// </summary>
/// <remarks>
/// ---------------------------------------------------------------------------
/// WHAT IS NOT HERE IS THE INTERESTING PART.
///
/// No email. No date of birth, no passport anything, no bank details - the
/// Django Profile carries all of those and this is a list handed to every
/// signed-in visitor. ProfileDto could afford to be generous because it only
/// ever describes the caller to themselves. This one describes strangers, so
/// it lists the four things a delegate list has always shown on a badge:
/// name, institution, position, country.
///
/// The general rule, worth stating once: a DTO's field list is an access
/// control decision, not a convenience. Serialising the entity here would
/// publish every column on Profile to every signed-in user, and would publish
/// each new column the day it was added, silently, forever.
/// ---------------------------------------------------------------------------
/// </remarks>
public sealed record EventParticipantDto
{
    public required int Id { get; init; }
    public required int ProfileId { get; init; }

    /// <summary>"Ada Lovelace", or "Participant 41" when the profile has no name.</summary>
    public required string DisplayName { get; init; }

    public string? Institution { get; init; }
    public string? Position { get; init; }
    public string? Country { get; init; }

    public required ParticipantRole Role { get; init; }
    public required ParticipationStatus Status { get; init; }
    public ParticipationMode? Mode { get; init; }
    public required bool CheckedIn { get; init; }

    /// <summary>True when this row is the caller's own.</summary>
    /// <remarks>
    /// Lets the table mark "you" without the client having to know its own
    /// profile id. Computed server-side because the server already knows who
    /// asked, and an id the client compares is an id the client must be sent.
    /// </remarks>
    public required bool IsMe { get; init; }
}
