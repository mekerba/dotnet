using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Remus.Mvc.Api.Dto;
using Remus.Mvc.Data;

namespace Remus.Mvc.Api;

// ============================================================================
// GET    /api/events                      the table
// GET    /api/events/{id}                 one event
// GET    /api/events/{id}/participants    who is going
// POST   /api/events/{id}/enrolment       sign me up
// DELETE /api/events/{id}/enrolment       never mind
//
// ---------------------------------------------------------------------------
// THIS GROUP HAS NO .RequireAuthorization(), AND THAT IS THE DESIGN.
//
// ProfileEndpoints puts it on the group, because every endpoint in it is about
// the caller and an anonymous caller has no profile. Here the rule genuinely
// differs per endpoint:
//
//     GET /api/events               anonymous. A catalogue is public.
//     GET /api/events/{id}          anonymous. Same.
//     GET .../participants          signed in. A delegate list is not public.
//     POST/DELETE .../enrolment     signed in. Obviously.
//
// So each endpoint states its own rule, the way AuthEndpoints does. The cost
// is that forgetting one is silent - there is no compiler and no test that
// notices. Django has the identical exposure with @login_required, and the
// identical mitigation: keep the list short enough to read in one screen.
//
// The list endpoint then answers DIFFERENTLY for the two cases - MyEnrolment
// is populated for a signed-in caller and null otherwise - which is the API
// shape of what a .cshtml expresses as @if (User.Identity.IsAuthenticated).
// One endpoint, two audiences, one branch.
// ---------------------------------------------------------------------------
// ============================================================================

public static class EventEndpoints
{
    public static RouteGroupBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/events")
            .WithTags("Events");

        group.MapGet("", List)
            .WithName("ListEvents")
            .WithSummary("The event catalogue, newest start date first.");

        group.MapGet("/{id:int}", Detail)
            .WithName("GetEvent")
            .WithSummary("One event, with its prose.");

        group.MapGet("/{id:int}/participants", Participants)
            .RequireAuthorization()
            .WithName("ListEventParticipants")
            .WithSummary("Who is enrolled. Signed-in callers only.");

        // The two writes, and the only endpoints here that need a CSRF token.
        group.MapPost("/{id:int}/enrolment", Enrol)
            .RequireAuthorization()
            .WithName("EnrolInEvent")
            .WithSummary("Enrol the signed-in profile in this event.")
            .AddEndpointFilter<AntiforgeryFilter>();

        group.MapDelete("/{id:int}/enrolment", Withdraw)
            .RequireAuthorization()
            .WithName("WithdrawFromEvent")
            .WithSummary("Withdraw the signed-in profile's application.")
            .AddEndpointFilter<AntiforgeryFilter>();

        return group;
    }

    // ======================================================================
    // GET /api/events
    // ======================================================================
    /// <remarks>
    /// ---------------------------------------------------------------------
    /// EVERY FILTER IS OPTIONAL AND EVERY ONE IS APPLIED IN SQL.
    ///
    /// The `if (x is not null) query = query.Where(...)` shape below is
    /// exactly Django's:
    ///
    ///     qs = Event.objects.all()
    ///     if status: qs = qs.filter(status=status)
    ///     if q:      qs = qs.filter(title__icontains=q)
    ///
    /// and it works for the same reason: an IQueryable, like a QuerySet, is a
    /// description of a query rather than its result. Nothing has executed
    /// when these lines run. The database is touched once, at ToListAsync,
    /// with every Where folded into one WHERE clause.
    ///
    /// The way to destroy that property is the same in both frameworks: call
    /// something that forces execution in the middle - .ToList(), or an
    /// operator the provider cannot translate - and every later filter runs in
    /// memory over every row in the table. In Django the tell is a
    /// list-of-models in the middle of a chain; in EF it is
    /// .AsEnumerable(), or an unsupported expression, which used to silently
    /// switch to client evaluation and since EF Core 3 throws instead. Being
    /// thrown at is a considerable improvement.
    ///
    /// `upcoming` is the one filter that could NOT be written as
    /// `.Where(e => !e.IsExpired)`. IsExpired is a C# property, and no
    /// provider can turn arbitrary C# into SQL - EF would throw. So the
    /// comparison is written out against the column instead, with today's date
    /// computed once, here, rather than per row. Django hits this wall in the
    /// same place and answers it the same way, with
    /// `filter(end_date__gte=date.today())`.
    /// ---------------------------------------------------------------------
    ///
    /// ORDERING IS STATED, NOT INHERITED.
    ///
    /// Django's Meta.ordering = ['-start_date'] attaches an ORDER BY to every
    /// query against the model, including ones that did not want one and
    /// including subqueries, which is a well-known source of quietly expensive
    /// SQL. EF has no equivalent and offers none. The ordering is here, in the
    /// query that wants it, which is more typing and never a surprise.
    /// </remarks>
    private static async Task<Ok<EventListItemDto[]>> List(
        ClaimsPrincipal user,
        ApplicationDbContext db,
        CancellationToken ct,
        bool? upcoming = null,
        EventStatus? status = null,
        EventType? type = null,
        string? q = null)
    {
        // Null for an anonymous caller, and null for a signed-in user whose
        // profile row is missing - which UserRegistrationService makes
        // impossible, but a nullable int says so without needing to be trusted.
        var profileId = await CurrentProfileIdAsync(user, db, ct);

        var query = db.Events.AsNoTracking();

        if (upcoming == true)
            query = query.Where(e => e.EndDate >= DateOnly.FromDateTime(DateTime.UtcNow));

        if (status is not null)
            query = query.Where(e => e.Status == status);

        if (type is not null)
            query = query.Where(e => e.Type == type);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();

            // EF.Functions.ILike is Npgsql's ILIKE - a case-insensitive LIKE
            // that only Postgres has. Django spells it __icontains and emits
            // UPPER(x) LIKE UPPER(y) on backends without it. Neither can use a
            // plain btree index, so this is a sequential scan; at catalogue
            // size that is correct and cheap, and the fix when it stops being
            // either is a trigram index, not a different query.
            query = query.Where(e =>
                EF.Functions.ILike(e.Title, $"%{term}%") ||
                EF.Functions.ILike(e.Code!, $"%{term}%") ||
                EF.Functions.ILike(e.City!, $"%{term}%"));
        }

        var events = await query
            .OrderByDescending(e => e.StartDate)
            .ThenBy(e => e.Title)
            .Select(e => new EventListItemDto
            {
                Id = e.Id,
                Code = e.Code,
                Title = e.Title,
                ShortTitle = e.ShortTitle,
                Type = e.Type,
                Status = e.Status,
                Level = e.Level,
                Nature = e.Nature,
                AttendanceNature = e.AttendanceNature,
                HostCountry = e.HostCountry,
                City = e.City,
                Venue = e.Venue,
                Language = e.Language,
                StartDate = e.StartDate,
                EndDate = e.EndDate,
                SeatsLimit = e.SeatsLimit,

                // A correlated subquery - SELECT COUNT(*) FROM
                // "EventParticipants" WHERE "EventId" = e."Id" - evaluated by
                // Postgres once per row of the outer query, not by fetching
                // the participants. Django's equivalent is
                // .annotate(Count("participants")); writing
                // `len(event.participants.all())` instead is the classic N+1,
                // and the EF equivalent of that mistake does not compile here
                // because there are no entities to walk.
                ParticipantCount = e.Participations.Count,

                // The caller's own row, also as a subquery, also once per row.
                // profileId is a captured local, so it goes into the SQL as a
                // parameter - and when it is null, EF folds the whole
                // subquery away rather than issuing it.
                MyEnrolment = profileId == null
                    ? null
                    : e.Participations
                       .Where(ep => ep.ProfileId == profileId)
                       .Select(ep => new MyEnrolmentDto
                       {
                           Id = ep.Id,
                           Status = ep.Status,
                           Role = ep.Role,
                           Mode = ep.Mode,
                           ApplicationDate = ep.ApplicationDate,
                           CheckedIn = ep.CheckedIn,
                           NeedVisa = ep.NeedVisa,
                       })
                       .FirstOrDefault(),
            })
            .ToArrayAsync(ct);

        return TypedResults.Ok(events);
    }

    // ======================================================================
    // GET /api/events/{id}
    // ======================================================================
    /// <remarks>
    /// The same projection as a list row, plus the fields a table has no room
    /// for. Note that EventDetailDto NESTS the list item rather than repeating
    /// its twenty properties - so adding a column to the table adds it here
    /// too, and the Angular client can hand `event.summary` to whatever
    /// renders a row without knowing it came from a detail call.
    /// </remarks>
    private static async Task<Results<Ok<EventDetailDto>, NotFound>> Detail(
        int id,
        ClaimsPrincipal user,
        ApplicationDbContext db,
        CancellationToken ct)
    {
        var profileId = await CurrentProfileIdAsync(user, db, ct);

        var detail = await db.Events
            .AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new EventDetailDto
            {
                Objectives = e.Objectives,
                HostInstitution = e.HostInstitution,
                Created = e.Created,
                LastUpdate = e.LastUpdate,
                Summary = new EventListItemDto
                {
                    Id = e.Id,
                    Code = e.Code,
                    Title = e.Title,
                    ShortTitle = e.ShortTitle,
                    Type = e.Type,
                    Status = e.Status,
                    Level = e.Level,
                    Nature = e.Nature,
                    AttendanceNature = e.AttendanceNature,
                    HostCountry = e.HostCountry,
                    City = e.City,
                    Venue = e.Venue,
                    Language = e.Language,
                    StartDate = e.StartDate,
                    EndDate = e.EndDate,
                    SeatsLimit = e.SeatsLimit,
                    ParticipantCount = e.Participations.Count,
                    MyEnrolment = profileId == null
                        ? null
                        : e.Participations
                           .Where(ep => ep.ProfileId == profileId)
                           .Select(ep => new MyEnrolmentDto
                           {
                               Id = ep.Id,
                               Status = ep.Status,
                               Role = ep.Role,
                               Mode = ep.Mode,
                               ApplicationDate = ep.ApplicationDate,
                               CheckedIn = ep.CheckedIn,
                               NeedVisa = ep.NeedVisa,
                           })
                           .FirstOrDefault(),
                },
            })
            .FirstOrDefaultAsync(ct);

        return detail is null ? TypedResults.NotFound() : TypedResults.Ok(detail);
    }

    // ======================================================================
    // GET /api/events/{id}/participants
    // ======================================================================
    /// <remarks>
    /// ---------------------------------------------------------------------
    /// THE PROJECTION STOPS WHERE SQL STOPS, AND THIS IS THE SEAM.
    ///
    /// Every other query in this file projects straight into its DTO. This one
    /// projects into an anonymous type first and builds the DTO afterwards,
    /// because DisplayName is not a column - it is "first and last joined,
    /// unless both are blank, in which case say Participant 41". Postgres
    /// could express that (COALESCE, NULLIF, concatenation) and EF might even
    /// translate it, but the SQL would be unreadable and the rule would live
    /// in a place no one thinks to look for a naming rule.
    ///
    /// So: the database does the part a database is for - filter, join, sort,
    /// send back twelve narrow rows - and the process does the part that is
    /// just C#. The important property is that the switch happens EXACTLY at
    /// ToListAsync and not one line earlier, so nothing is filtered in memory.
    ///
    /// Django's version of this seam is the same and less visible: `.values()`
    /// gives you dicts and anything past that is Python. The trap there is
    /// that forgetting `.values()` still works, just slower and with an N+1
    /// hiding in a `@property`.
    /// ---------------------------------------------------------------------
    ///
    /// SORTED BY SURNAME, which is not the database's opinion and not
    /// Django's Meta.ordering (that one says `['event']`, which for a list
    /// filtered to one event orders by nothing at all). A delegate list is
    /// read by a human looking for a name.
    /// </remarks>
    private static async Task<Results<Ok<EventParticipantDto[]>, NotFound>> Participants(
        int id,
        ClaimsPrincipal user,
        ApplicationDbContext db,
        CancellationToken ct)
    {
        // A missing event and an event with no participants are different
        // answers - 404 versus an empty array - and the client shows different
        // things for them. Without this check both would be [].
        var exists = await db.Events.AnyAsync(e => e.Id == id, ct);
        if (!exists) return TypedResults.NotFound();

        var profileId = await CurrentProfileIdAsync(user, db, ct);

        var rows = await db.EventParticipants
            .AsNoTracking()
            .Where(ep => ep.EventId == id)
            .OrderBy(ep => ep.Profile.LastName)
            .ThenBy(ep => ep.Profile.FirstName)
            .Select(ep => new
            {
                ep.Id,
                ep.ProfileId,
                ep.Role,
                ep.Status,
                ep.Mode,
                ep.CheckedIn,
                ep.Profile.FirstName,
                ep.Profile.LastName,
                ep.Profile.Institution,
                ep.Profile.Position,
                ep.Profile.Country,
            })
            .ToListAsync(ct);

        var participants = rows
            .Select(r => new EventParticipantDto
            {
                Id = r.Id,
                ProfileId = r.ProfileId,
                DisplayName = DisplayName(r.FirstName, r.LastName, r.ProfileId),
                Institution = r.Institution,
                Position = r.Position,
                Country = r.Country,
                Role = r.Role,
                Status = r.Status,
                Mode = r.Mode,
                CheckedIn = r.CheckedIn,
                IsMe = r.ProfileId == profileId,
            })
            .ToArray();

        return TypedResults.Ok(participants);
    }

    // ======================================================================
    // POST /api/events/{id}/enrolment
    // ======================================================================
    /// <remarks>
    /// ---------------------------------------------------------------------
    /// WHY THIS IS A SUB-RESOURCE AND NOT /api/enrolments.
    ///
    /// The enrolment being created belongs to exactly one event and to exactly
    /// one caller, and both are already known before the body is read - the
    /// event from the route, the profile from the auth cookie. A top-level
    /// /api/enrolments taking {eventId, profileId} would be the same operation
    /// with two more chances to be lied to. The URL is the safer place to put
    /// a value that the server is going to check anyway.
    ///
    /// DELETE then has somewhere obvious to live, and it is the reason the
    /// path is `/enrolment` singular: there is at most one, per caller, per
    /// event, and the unique index says so.
    /// ---------------------------------------------------------------------
    ///
    /// FOUR REFUSALS, AND ONLY ONE OF THEM IS A RACE.
    ///
    ///   no profile        404. Cannot enrol what does not exist.
    ///   no event          404.
    ///   event not open    409. Draft, cancelled, over, or full.
    ///   already enrolled  409, twice: once by the check below, once by the
    ///                     unique index if two requests arrive together. The
    ///                     check exists for the message; the index exists for
    ///                     the correctness. See EventParticipantConfiguration.
    /// </remarks>
    private static async Task<Results<Created<MyEnrolmentDto>, NotFound, ProblemHttpResult>> Enrol(
        int id,
        EnrolmentRequestDto? input,
        ClaimsPrincipal user,
        ApplicationDbContext db,
        ILogger<EnrolmentRequestDto> logger,
        CancellationToken ct)
    {
        var profileId = await CurrentProfileIdAsync(user, db, ct);
        if (profileId is null) return TypedResults.NotFound();

        // Tracked, not AsNoTracking: the new EventParticipant points at this
        // event, and the participant count is read off it.
        var @event = await db.Events
            .Include(e => e.Participations)
            .FirstOrDefaultAsync(e => e.Id == id, ct);

        if (@event is null) return TypedResults.NotFound();

        if (@event.Participations.Any(ep => ep.ProfileId == profileId))
            return TypedResults.Problem(
                title: "Already enrolled.",
                detail: "This profile already has an application for this event.",
                statusCode: StatusCodes.Status409Conflict);

        if (ClosedReason(@event) is { } reason)
            return TypedResults.Problem(
                title: "Enrolment is closed.",
                detail: reason,
                statusCode: StatusCodes.Status409Conflict);

        var enrolment = new EventParticipant
        {
            EventId = @event.Id,
            ProfileId = profileId.Value,

            // Server-decided, every one of them. None of these is bound from
            // the request, which is the whole argument in EnrolmentRequestDto.
            Status = ParticipationStatus.Applied,
            Role = ParticipantRole.Participant,
            ApplicationDate = DateOnly.FromDateTime(DateTime.UtcNow),

            // NULL unless the event actually offers a choice. See the note on
            // EnrolmentRequestDto.Mode: a Physical event has one possible mode
            // and storing it would duplicate AttendanceNature.
            Mode = IsHybrid(@event) ? input?.Mode : null,

            NeedVisa = input?.NeedVisa ?? false,
            Comments = input?.Comments?.Trim(),
        };

        db.EventParticipants.Add(enrolment);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // The race the check above cannot close: two requests both saw no
            // enrolment, both inserted, and the unique index refused the
            // second. The caller gets the same 409 either way, which is the
            // point - they do not care which mechanism caught it.
            logger.LogInformation(
                "Concurrent enrolment for profile {ProfileId} in event {EventId} was refused by the unique index.",
                profileId, id);

            return TypedResults.Problem(
                title: "Already enrolled.",
                detail: "This profile already has an application for this event.",
                statusCode: StatusCodes.Status409Conflict);
        }

        logger.LogInformation(
            "Profile {ProfileId} enrolled in event {EventCode}.", profileId, @event.Code);

        var body = new MyEnrolmentDto
        {
            Id = enrolment.Id,
            Status = enrolment.Status,
            Role = enrolment.Role,
            Mode = enrolment.Mode,
            ApplicationDate = enrolment.ApplicationDate,
            CheckedIn = enrolment.CheckedIn,
            NeedVisa = enrolment.NeedVisa,
        };

        // 201 with a Location header, rather than the 200 the profile PUT
        // returns. PUT replaced something that already existed; this made
        // something that did not, and the URL where it now lives is worth
        // saying out loud even though this particular client does not read it.
        return TypedResults.Created($"{ApiSetup.Prefix}/events/{id}/enrolment", body);
    }

    // ======================================================================
    // DELETE /api/events/{id}/enrolment
    // ======================================================================
    /// <remarks>
    /// WITHDRAWING IS ONLY YOURS TO DO WHILE NOBODY HAS ACTED ON IT.
    ///
    /// Once an operator has moved the row to Selected or Accepted, a seat has
    /// been allocated and possibly a flight booked. Deleting it from a browser
    /// at that point is not a withdrawal, it is a disappearance - so the
    /// endpoint refuses and says who to talk to. ParticipationStatus.Withdrawn
    /// exists for the operator-side action that supersedes it, which has no
    /// endpoint yet.
    ///
    /// THE ROW IS DELETED, NOT FLAGGED. An application that was withdrawn
    /// before anyone looked at it is not history worth keeping, and keeping it
    /// would mean the unique index refuses the re-application that usually
    /// follows about thirty seconds later. Django's EventParticipantLog is
    /// where the audit trail would go if one were wanted; it is not ported.
    ///
    /// 204, not 200: there is nothing left to describe.
    /// </remarks>
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> Withdraw(
        int id,
        ClaimsPrincipal user,
        ApplicationDbContext db,
        ILogger<EnrolmentRequestDto> logger,
        CancellationToken ct)
    {
        var profileId = await CurrentProfileIdAsync(user, db, ct);
        if (profileId is null) return TypedResults.NotFound();

        var enrolment = await db.EventParticipants
            .FirstOrDefaultAsync(ep => ep.EventId == id && ep.ProfileId == profileId, ct);

        // 404 and not 204. DELETE is idempotent in the sense that deleting
        // twice leaves the same state, but "you were never enrolled" and "you
        // are no longer enrolled" are different facts and the UI shows
        // different things for them.
        if (enrolment is null) return TypedResults.NotFound();

        if (enrolment.Status != ParticipationStatus.Applied)
            return TypedResults.Problem(
                title: "This application can no longer be withdrawn here.",
                detail: $"The organiser has already moved it to {enrolment.Status}. "
                      + "Contact them to cancel your participation.",
                statusCode: StatusCodes.Status409Conflict);

        db.EventParticipants.Remove(enrolment);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Profile {ProfileId} withdrew from event {EventId}.", profileId, id);

        return TypedResults.NoContent();
    }

    // ======================================================================
    // helpers
    // ======================================================================

    /// <summary>
    /// The signed-in caller's profile id, or null when there is no caller.
    /// </summary>
    /// <remarks>
    /// ClaimsPrincipal.CurrentUserId() throws for an anonymous caller - it is
    /// written for endpoints behind .RequireAuthorization, where anonymous
    /// cannot happen. Two endpoints here allow anonymous, so the check comes
    /// first and the answer is nullable.
    ///
    /// This costs ONE QUERY on every request that needs it, because the auth
    /// cookie carries the Identity user id and the Profile id is a different
    /// number. Adding the profile id as a claim at sign-in would remove the
    /// query and add a staleness problem; not worth it at this size, but it is
    /// the usual next step and worth naming. Django pays this too - a
    /// `request.user.profile` is a query - it just never shows you the bill.
    /// </remarks>
    private static async Task<int?> CurrentProfileIdAsync(
        ClaimsPrincipal user,
        ApplicationDbContext db,
        CancellationToken ct)
    {
        if (user.Identity?.IsAuthenticated != true) return null;

        var userId = user.CurrentUserId();

        // The cast to int? matters. FirstOrDefaultAsync on a plain int returns
        // 0 for "no row", and 0 is a perfectly plausible id to then compare
        // against. Projecting to a nullable makes "missing" unmistakable.
        return await db.Profiles
            .Where(p => p.UserId == userId)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Why this event is not accepting applications, or null if it is.</summary>
    /// <remarks>
    /// One method, so the four reasons cannot drift apart, and it returns the
    /// SENTENCE rather than a bool so the caller does not have to re-derive
    /// which rule fired. EventListItemDto.IsOpenForEnrolment is the client-side
    /// echo of the same rule - it greys out the button; this refuses the write.
    /// </remarks>
    private static string? ClosedReason(Event @event) => @event switch
    {
        { Status: not EventStatus.Approved } e =>
            $"This event is {e.Status.ToString().ToLowerInvariant()}, not open for applications.",

        { } e when e.IsExpired =>
            $"This event ended on {e.EndDate:d MMMM yyyy}.",

        { SeatsLimit: > 0 } e when e.Participations.Count >= e.SeatsLimit =>
            $"All {e.SeatsLimit} seats are taken.",

        _ => null,
    };

    private static bool IsHybrid(Event @event) =>
        @event.AttendanceNature is AttendanceNature.Hybrid or AttendanceNature.HybridStreaming;

    /// <summary>
    /// 23505 is Postgres's unique_violation. Everything else rethrows.
    /// </summary>
    /// <remarks>
    /// Matching on the SQLSTATE rather than on the message text, which is
    /// localised and changes between releases. Django's equivalent is catching
    /// IntegrityError, which is broader - it covers foreign key and check
    /// violations too, so a Django `except IntegrityError` around an insert
    /// quietly swallows a class of bugs this does not.
    /// </remarks>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };

    /// <summary>Profile.DisplayName, for a profile that was never loaded.</summary>
    /// <remarks>
    /// The entity has this rule as a computed property, and it falls back to
    /// the user's EMAIL when there is no name. That fallback is wrong here -
    /// this list is shown to other participants and an email is not theirs to
    /// see. So the rule is re-stated with a neutral fallback rather than
    /// reused, and the duplication is the honest signal that two audiences
    /// need two answers.
    /// </remarks>
    private static string DisplayName(string? firstName, string? lastName, int profileId)
    {
        var full = string.Join(" ", new[] { firstName, lastName }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        return string.IsNullOrWhiteSpace(full) ? $"Participant {profileId}" : full;
    }
}
