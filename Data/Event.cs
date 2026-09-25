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
/// A training, workshop, conference or meeting that profiles can enrol in.
/// </summary>
/// <remarks>
/// Django equivalent: events/models.py's Event in remus_local, reduced to the
/// columns the listing and the enrolment flow actually use. What was left out,
/// and why, is recorded in remus-angular/PROPOSAL-EVENTS.md - the short
/// version is that the six deadline dates, the six publish/enable
/// flags, the financial/travel/visa prose and the three audit-log tables
/// belong to an operator back-office that does not exist here yet.
///
/// ---------------------------------------------------------------------------
/// THE THREE THINGS THAT DO NOT SURVIVE THE PORT UNCHANGED
///
/// 1. `participants = models.ManyToManyField("users.Profile",
///                                           through='EventParticipant')`
///
///    Django gives you the collection AND the through model from one line, and
///    `event.participants.all()` silently joins across the through table. EF
///    needs both halves declared - Participants (the skip navigation, below)
///    and Participations (the rows themselves) - plus a .UsingEntity call in
///    EventConfiguration to tell it they are two views of one relationship.
///    Three declarations where Django had one. In exchange, the payload
///    columns on EventParticipant are reachable from a typed navigation
///    instead of through `event.eventparticipant_set`.
///
/// 2. The `code` generator in save(). Django hangs it off the model's own save,
///    because a Django model IS its own persistence. EF entities are passive -
///    nothing on this class is ever called by the database layer - so the
///    generator lives where every other save-time hook in this project lives:
///    the SaveChangesAsync override on ApplicationDbContext, next to the
///    auto_now stamping. The pieces it needs (CodeToken, BuildCode) stay here,
///    as ordinary methods, so the rule reads next to the column it fills.
///
/// 3. `type`, `division`, `section`, `sub_type` were ForeignKeys to a generic
///    Lookup table. Only Type survives, as an enum. See EventEnums.cs.
/// ---------------------------------------------------------------------------
/// </remarks>
public class Event : IAuditedEntity
{
    public int Id { get; set; }

    // ---- identity ------------------------------------------------------------

    /// <summary>
    /// Human-readable unique event code, e.g. <c>TRN-20260610-01</c>.
    /// </summary>
    /// <remarks>
    /// Format: <c>&lt;TYPE&gt;-&lt;YYYYMMDD&gt;-&lt;NN&gt;</c>, assigned once on
    /// insert and never re-derived - editing the date or the type afterwards
    /// does not churn it. Nullable purely so that the assignment can happen
    /// after the object is constructed; a row that has been through
    /// SaveChanges always has one.
    ///
    /// Django's field is <c>editable=False</c>, which keeps it out of every
    /// ModelForm. The .NET equivalent is not an attribute on the property - it
    /// is the absence of this property from EventDto's writable twin. Nothing
    /// binds to an entity here, so there is nothing to exclude it from.
    /// </remarks>
    public string? Code { get; set; }

    public string Title { get; set; } = null!;
    public string? ShortTitle { get; set; }

    /// <summary>The aims. Django: TextField(max_length=2000).</summary>
    /// <remarks>
    /// Note that <c>max_length</c> on a Django TextField is a FORM constraint
    /// only - the column is `text` and a 5000-character string written through
    /// the ORM goes straight in. HasMaxLength in EventConfiguration emits
    /// varchar(2000), so the database refuses it. The .NET version is the
    /// stricter of the two, which is worth knowing when porting data.
    /// </remarks>
    public string? Objectives { get; set; }

    public EventType Type { get; set; }

    // ---- where -----------------------------------------------------------------
    // ISO 3166-1 alpha-2, same treatment as Profile.Country: a string rather
    // than an enum, because there are ~250 of them and they change. The Django
    // model spells out at length why it carries no `choices` - pycountry labels
    // drift between releases and re-emit a no-op migration every time. EF has
    // the same failure mode for a different reason: an enum's members are its
    // schema, so adding a country would be a migration.
    public string? HostCountry { get; set; }
    public string? City { get; set; }
    public string? Venue { get; set; }
    public string? HostInstitution { get; set; }

    /// <summary>ISO 639-1 alpha-2. Django: choices=LANGUAGE_CHOICES.</summary>
    public string? Language { get; set; }

    // ---- when ------------------------------------------------------------------
    // DateOnly, not DateTime - the Postgres type is `date`, matching Django's
    // DateField. DateTime would silently give you `timestamp` and a midnight
    // component nobody asked for.
    //
    // NON-NULLABLE, where Django has null=True, blank=False. That pair means
    // "the database allows NULL but no form may submit one" - a compromise for
    // rows that predate the field. There is no legacy here, so the column says
    // what it means.
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    // ---- classification --------------------------------------------------------
    public EventStatus Status { get; set; } = EventStatus.Draft;
    public EventLevel Level { get; set; } = EventLevel.Introductory;
    public EventNature Nature { get; set; } = EventNature.International;
    public AttendanceNature AttendanceNature { get; set; } = AttendanceNature.Physical;

    /// <summary>0 means unlimited, matching the Django default.</summary>
    /// <remarks>
    /// Django backs this with <c>validators=[MinValueValidator(0)]</c>, which
    /// - like <c>choices</c> - runs in full_clean() and not in the database.
    /// EventConfiguration adds a real CHECK constraint instead, so a negative
    /// value is refused by Postgres however it arrives.
    /// </remarks>
    public int SeatsLimit { get; set; }

    // ---- the many-to-many, both halves -----------------------------------------

    /// <summary>
    /// The enrolment rows. Django: <c>event.eventparticipant_set</c>.
    /// </summary>
    /// <remarks>
    /// This is the one to use whenever the payload matters - status, role,
    /// dates, check-in - which in this application is almost always.
    /// </remarks>
    public ICollection<EventParticipant> Participations { get; set; } = [];

    /// <summary>
    /// The profiles themselves, skipping the join. Django:
    /// <c>event.participants</c>.
    /// </summary>
    /// <remarks>
    /// EF calls this a SKIP NAVIGATION, and it is the closest thing to
    /// ManyToManyField's collection: <c>event.Participants</c> reads across
    /// EventParticipant without mentioning it, exactly as
    /// <c>event.participants.all()</c> does.
    ///
    /// It is declared here for the symmetry with Django and used sparingly.
    /// The catch is the same one Django has: adding to this collection creates
    /// a join row with DEFAULT payload - Applied, Participant, no dates - and
    /// there is no place to say otherwise. Django makes that an error outright
    /// (`.add()` is refused on a through= relation without through_defaults).
    /// EF allows it silently. EventEndpoints therefore always inserts an
    /// EventParticipant explicitly.
    /// </remarks>
    public ICollection<Profile> Participants { get; set; } = [];

    // ---- audit -----------------------------------------------------------------
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset LastUpdate { get; set; }

    // ---- computed ---------------------------------------------------------------

    /// <summary>True once EndDate is in the past, independent of Status.</summary>
    /// <remarks>
    /// Django's <c>is_expired</c> property, and it carries the same warning:
    /// this is evaluated in memory, so it cannot appear in a WHERE clause. A
    /// query that needs it has to compare EndDate to today's date itself - see
    /// the note on EventEndpoints' `upcoming` filter, which does exactly that
    /// rather than pulling every row into the process to ask each one.
    /// </remarks>
    public bool IsExpired => EndDate < DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Title for a list or a picker, never empty.</summary>
    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(ShortTitle) ? ShortTitle : Title;

    // ---- the code generator ------------------------------------------------------

    /// <summary>The 3-char token that opens the event code.</summary>
    /// <remarks>
    /// Django reads it out of EVENT_TYPE_CODE_MAP, keyed on the lowercased
    /// Lookup name, and falls back to the first three alphanumerics padded
    /// with X when the name is not in the map. There is no fallback here and
    /// there cannot be one: Type is an enum, so the set of inputs is closed
    /// and the compiler checks that this switch covers it.
    /// </remarks>
    public string CodeToken => Type switch
    {
        EventType.Training => "TRN",
        EventType.Workshop => "WKS",
        EventType.Conference => "CNF",
        EventType.Roster => "RST",
        EventType.Symposium => "SYM",
        EventType.Webinar => "WBN",
        EventType.Exercise => "EXR",
        EventType.ELearning => "ELN",
        EventType.TechnicalMeeting => "TEM",
        EventType.Meeting => "MTG",
        EventType.Seminar => "SMN",
        EventType.Course => "CRS",
        _ => "EVT",
    };

    /// <summary>The <c>TYPE-YYYYMMDD</c> half of the code, without the counter.</summary>
    public string CodeBase => $"{CodeToken}-{StartDate:yyyyMMdd}";

    /// <summary>
    /// Picks the lowest free two-digit counter for this event's base, given the
    /// codes already taken.
    /// </summary>
    /// <remarks>
    /// Django's <c>_build_code</c>, with the query lifted out. The Django
    /// version runs <c>Event.objects.filter(code__startswith=base)</c> inline,
    /// because a Django model can reach its own manager. An EF entity cannot
    /// reach a DbContext and should not try - so the caller does the query and
    /// passes the answer in, which has the side effect of making this method
    /// trivially testable with no database at all.
    ///
    /// "Lowest free" rather than "highest plus one" is deliberate in both
    /// versions: codes stay dense after a delete.
    /// </remarks>
    public string BuildCode(IEnumerable<string> takenCodes)
    {
        var used = takenCodes
            .Select(c => c.Split('-').Last())
            .Where(suffix => int.TryParse(suffix, out _))
            .Select(int.Parse)
            .ToHashSet();

        var n = 1;
        while (used.Contains(n)) n++;

        return $"{CodeBase}-{n:D2}";
    }
}
