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

// ---------------------------------------------------------------------------
// The choice lists from remus_local's events/models.py, as C# enums.
//
// Django spells these three different ways in one file, and the .NET port
// flattens all three into one mechanism:
//
//   1. A module-level list of tuples on the model:
//
//          STATUS = [("draft", "Draft"), ("approved", "Approved"), ...]
//          status = models.CharField(max_length=20, choices=STATUS,
//                                    default="draft")
//
//      The database column is a plain varchar. `choices` is validated by
//      full_clean(), i.e. by forms - a `.update(status="banana")` writes
//      banana. The C# enum below cannot hold banana at all; there is no
//      validation step because there is no way to express the invalid value.
//
//   2. A ForeignKey to a generic lookup table:
//
//          type = models.ForeignKey(Lookup, on_delete=models.PROTECT,
//                                   related_name="event_type")
//          role = models.ForeignKey(Detailed_Lookup, ...,
//                                   limit_choices_to={'category__name': 'ParticipantRole'})
//
//      Two tables (Lookup, Detailed_Lookup) holding every category of every
//      dropdown in the application, discriminated by a `category` column.
//      Editable by an operator without a deploy - which is the real argument
//      for it - at the cost of a JOIN on every read, a `limit_choices_to`
//      incantation on every field, and no compile-time check anywhere.
//
//      EventType, ParticipantRole and ParticipationStatus below are that
//      pattern collapsed into enums. The trade is deliberate and it is the
//      one thing to revisit first if these lists ever need to change without
//      a deploy: an enum is a code change and a migration, a lookup row is an
//      INSERT.
//
//   3. remus_choices constants (UN_LANGUAGES, LANGUAGE_CHOICES), which stay
//      strings here for the same reason Profile.Country does - there are ~250
//      of them and they drift between pycountry releases.
//
// EVERY ONE OF THESE IS STORED AS ITS NAME, not its ordinal, via
// .HasConversion<string>() in EventConfiguration. See the long note in
// ProfileEnums.cs: storing ints is smaller and unreadable, and renumbering the
// enum silently repoints existing rows.
// ---------------------------------------------------------------------------

/// <summary>Django: Event.type -> Lookup, via EVENT_TYPE_CODE_MAP.</summary>
/// <remarks>
/// The curated map in the Django model turns the lookup's name into the 3-char
/// token that opens an event code ("training" -> "TRN"). Here the enum IS the
/// curated list, so the token lives on it - see Event.CodeToken.
/// </remarks>
public enum EventType
{
    Training,
    Workshop,
    Conference,
    Roster,
    Symposium,
    Webinar,
    Exercise,
    ELearning,
    TechnicalMeeting,
    Meeting,
    Seminar,
    Course,
}

/// <summary>Django: Event.STATUS.</summary>
public enum EventStatus
{
    Draft,
    Reviewed,
    Approved,
    Tentative,
    Canceled,
}

/// <summary>Django: Event.LEVEL.</summary>
public enum EventLevel
{
    Introductory,
    Intermediate,
    Advanced,
    Other,
}

/// <summary>Django: Event.NATURE.</summary>
public enum EventNature
{
    International,
    National,
    Regional,
}

/// <summary>Django: Event.ATTENDANCE_NATURE.</summary>
/// <remarks>
/// Django stores "online_streaming" and displays "Online (Streaming)". A C#
/// enum member cannot contain an underscore-separated lowercase pair without
/// looking wrong to every other .NET reader, so the stored value is the
/// PascalCase name and the display string is the client's business. The
/// Angular app writes its own labels; so did the .cshtml pages.
/// </remarks>
public enum AttendanceNature
{
    Physical,
    Online,
    OnlineStreaming,
    Hybrid,
    HybridStreaming,
}

/// <summary>
/// Django: EventParticipant.role -> Detailed_Lookup, category "ParticipantRole".
/// </summary>
public enum ParticipantRole
{
    Participant,
    Lecturer,
    Observer,
    Organiser,
    Coordinator,
}

/// <summary>
/// Django: EventParticipant.participation_status -> Detailed_Lookup,
/// category "ParticipantStatus".
/// </summary>
/// <remarks>
/// The workflow these names describe is the one implied by the three date
/// columns on EventParticipant: ApplicationDate (Applied), SelectionDate
/// (Selected), AcceptanceDate (Accepted). A participant enrolling through
/// POST /api/events/{id}/enrolment starts at Applied - the API deliberately
/// does not let the caller choose, which is the whole over-posting argument
/// from ProfileUpdateDto applied to a workflow column.
/// </remarks>
public enum ParticipationStatus
{
    Applied,
    Selected,
    Accepted,
    Rejected,
    Withdrawn,
}

/// <summary>Django: EventParticipant.PARTICIPATION_MODE_CHOICES.</summary>
/// <remarks>
/// Nullable on the entity, exactly as in Django: the question is only asked
/// for Hybrid / HybridStreaming events, so NULL means "never applicable here"
/// rather than "not answered yet". That distinction is why this is a
/// ParticipationMode? and not a mode with a `NotAsked` member.
/// </remarks>
public enum ParticipationMode
{
    Online,
    InPerson,
}
