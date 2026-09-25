using System.ComponentModel.DataAnnotations;
using Remus.Mvc.Data;

namespace Remus.Mvc.Api.Dto;

/// <summary>
/// What POST /api/events/{id}/enrolment accepts. The whole of it.
/// </summary>
/// <remarks>
/// ---------------------------------------------------------------------------
/// THREE FIELDS, AND THE OMISSIONS ARE THE DESIGN.
///
/// EventParticipant has sixteen columns. A caller may set three of them. The
/// other thirteen are not "hidden from the form" - there is no form - they are
/// absent from the only type this endpoint will bind, which is the only thing
/// that actually refuses them.
///
/// Take them one at a time, because each is a different kind of refusal:
///
///   Status            an APPROVAL. Accepting your own application is the
///                     whole point of the workflow. Always Applied on insert;
///                     moving it is an operator action with no endpoint yet.
///   Role              an APPOINTMENT. Lecturers are invited, not self-
///                     declared. Always Participant.
///   EventId           comes from the URL, not the body. Two sources for one
///                     value is two sources to disagree - and the URL is the
///                     one .RequireAuthorization and the route pattern have
///                     already seen.
///   ProfileId         comes from the AUTH COOKIE. This is the important one:
///                     accept it from the body and any signed-in visitor can
///                     enrol anybody. The same rule as ProfileEndpoints
///                     scoping to the claim rather than to a ?userId=.
///   Selection/        operator dates.
///   AcceptanceDate
///   ApplicationDate   today, server-side. A client-supplied application date
///                     is a client-supplied place in the queue.
///   CheckedIn,        the day itself. Checking yourself in from a hotel room
///   CheckinDate       defeats the only thing a check-in measures.
///
/// Django's framing of the same idea is `fields = [...]` on a ModelForm and
/// never `"__all__"`. See the long note on ProfileUpdateDto; this endpoint is
/// the case where getting it wrong is not a data-quality bug but a privilege
/// escalation.
/// ---------------------------------------------------------------------------
///
/// AND IT IS OPTIONAL. The endpoint takes this body as nullable, so
///
///     POST /api/events/7/enrolment        (no body at all)
///
/// is a valid "sign me up, defaults are fine" - which is what the Enrol button
/// in the Angular table sends. The detail page sends the body.
/// </remarks>
public sealed record EnrolmentRequestDto
{
    /// <summary>
    /// Online or in person. Only meaningful for a Hybrid event.
    /// </summary>
    /// <remarks>
    /// The endpoint NULLS this out for a non-hybrid event rather than
    /// rejecting it, because the distinction is not the caller's mistake: a
    /// Physical event has exactly one possible mode and storing it would be
    /// storing a fact the AttendanceNature column already holds. Django's
    /// comment on the same field says the same thing - NULL means "never
    /// applicable here", not "not answered".
    /// </remarks>
    public ParticipationMode? Mode { get; init; }

    /// <summary>Whether the applicant expects to need a visa.</summary>
    public bool NeedVisa { get; init; }

    /// <summary>Anything the applicant wants the organiser to know.</summary>
    /// <remarks>
    /// 5000 to match the column, and the DataAnnotation is checked before the
    /// handler runs - builder.Services.AddValidation() in Program.cs is what
    /// arranges that, so there is no ModelState.IsValid to forget. A 5001-
    /// character comment comes back as 400 + ProblemDetails with the key
    /// "Comments", which problem-details.ts lower-cases to "comments" and the
    /// Angular form paints next to the textarea.
    /// </remarks>
    [StringLength(5000, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? Comments { get; init; }
}
