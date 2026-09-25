using Microsoft.EntityFrameworkCore;
using Remus.Mvc.Data;

namespace Remus.Mvc.Commands;

// ============================================================================
// seedevents  —  Django: manage.py loaddata events.json
//
// Django ships fixtures: a JSON or YAML file of rows, loaded by a command that
// already exists. .NET ships neither the format nor the command, so a fixture
// here is ordinary code in an ordinary command - see the long note at the top
// of CreateSuperUser.cs for why a management command means branching on args
// before app.Run().
//
// Writing it as code rather than as data is more typing and buys two things a
// fixture file cannot have:
//
//   * IT CANNOT DRIFT. A fixture names columns as strings. Rename Event.City
//     and every fixture mentioning it breaks at load time, in production, on
//     a Friday. This file stops compiling.
//
//   * IT IS IDEMPOTENT BY CONSTRUCTION. loaddata with explicit pks overwrites;
//     without them it duplicates on every run. The check below looks for the
//     row before writing it, so running this five times leaves ten events.
//
// The DATES ARE RELATIVE to the day it runs, which a fixture file also cannot
// do. A checked-in JSON file of 2026 dates is a catalogue of expired events by
// 2027; these stay a sensible mix of past, present and future forever.
//
//   dotnet run -- seedevents
//   dotnet run -- seedevents --reset     (delete every event first)
// ============================================================================
public static class SeedEvents
{
    public const string Verb = "seedevents";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var reset = false;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h":
                    Console.WriteLine($"""
                        Seeds a demo event catalogue.

                          Usage: dotnet run -- {Verb} [--reset]

                          --reset   Delete every existing event (and every enrolment,
                                    by cascade) before seeding. Destructive.
                        """);
                    return 0;

                case "--reset":
                    reset = true;
                    break;

                default:
                    Console.Error.WriteLine($"Unknown option '{args[i]}'.");
                    return 2;
            }
        }

        // Startup code runs outside any request, so there is no ambient DI
        // scope to take a scoped ApplicationDbContext from. Creating one by
        // hand is the fix, and forgetting to is the "Cannot consume scoped
        // service from singleton" error every .NET developer meets once.
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (reset)
        {
            // ExecuteDeleteAsync emits one DELETE statement and does not load a
            // single row into memory. Django's queryset .delete() does the
            // opposite by default: it fetches the objects so that CASCADE can
            // run in Python and post_delete signals can fire. The enrolments go
            // either way - that cascade is in the foreign key constraint, so
            // Postgres does it - but nothing in .NET gets a chance to react.
            var removed = await db.Events.ExecuteDeleteAsync();
            Console.WriteLine($"Deleted {removed} event(s).");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var seeded = 0;

        foreach (var (offsetDays, lengthDays, seed) in Catalogue())
        {
            seed.StartDate = today.AddDays(offsetDays);
            seed.EndDate = seed.StartDate.AddDays(lengthDays);

            // The idempotency check. Title plus start date, because Code is not
            // known until SaveChanges has assigned it - which is exactly the
            // point at which it is too late to check.
            var exists = await db.Events.AnyAsync(e =>
                e.Title == seed.Title && e.StartDate == seed.StartDate);

            if (exists) continue;

            db.Events.Add(seed);
            seeded++;
        }

        // ONE SaveChanges FOR THE WHOLE BATCH, and that is what makes the code
        // generator interesting: ApplicationDbContext sees every new event at
        // once, groups them by TYPE-YYYYMMDD, and hands out -01, -02, -03
        // within each group in a single pass. Django, saving one instance at a
        // time, runs the same query once per event.
        //
        // The catalogue below deliberately contains two workshops starting on
        // the same day, so a successful run prints a WKS-…-01 and a WKS-…-02
        // and proves it.
        await db.SaveChangesAsync();

        Console.WriteLine($"Seeded {seeded} new event(s); {await db.Events.CountAsync()} in total.");

        foreach (var e in await db.Events.OrderBy(e => e.Code).Select(e => new { e.Code, e.Title }).ToListAsync())
            Console.WriteLine($"  {e.Code,-18} {e.Title}");

        return 0;
    }

    /// <summary>
    /// The catalogue, as (days from today, length in days, the event).
    /// </summary>
    /// <remarks>
    /// Start and end dates are filled in by the caller so they move with the
    /// clock. Everything else is fixed.
    /// </remarks>
    private static IEnumerable<(int Offset, int Length, Event Event)> Catalogue() =>
    [
        (-120, 4, new Event
        {
            Title = "Introductory Course on Seismic Waveform Analysis",
            ShortTitle = "Seismic Waveform Analysis",
            Objectives = "Read, filter and interpret three-component seismic waveforms from "
                       + "International Monitoring System stations, and produce a defensible "
                       + "first-arrival pick.",
            Type = EventType.Course,
            Status = EventStatus.Approved,
            Level = EventLevel.Introductory,
            Nature = EventNature.International,
            AttendanceNature = AttendanceNature.Physical,
            HostCountry = "AT", City = "Vienna", Venue = "Vienna International Centre",
            HostInstitution = "CTBTO Preparatory Commission",
            Language = "en", SeatsLimit = 25,
        }),

        (-45, 2, new Event
        {
            Title = "Regional Workshop on Radionuclide Data Interpretation",
            Type = EventType.Workshop,
            Status = EventStatus.Approved,
            Level = EventLevel.Intermediate,
            Nature = EventNature.Regional,
            AttendanceNature = AttendanceNature.Hybrid,
            HostCountry = "MA", City = "Rabat", Venue = "CNESTEN",
            HostInstitution = "Centre National de l'Energie, des Sciences et des Techniques Nucleaires",
            Language = "fr", SeatsLimit = 30,
        }),

        (-10, 3, new Event
        {
            Title = "On-Site Inspection Tabletop Exercise",
            ShortTitle = "OSI Tabletop Exercise",
            Type = EventType.Exercise,
            Status = EventStatus.Approved,
            Level = EventLevel.Advanced,
            Nature = EventNature.International,
            AttendanceNature = AttendanceNature.Physical,
            HostCountry = "KZ", City = "Almaty", Venue = "Institute of Geophysical Research",
            Language = "en", SeatsLimit = 18,
        }),

        (14, 1, new Event
        {
            Title = "Webinar: Introduction to the International Data Centre",
            ShortTitle = "IDC Webinar",
            Objectives = "A one-hour orientation to the IDC's products, bulletins and the "
                       + "process that turns raw station data into a reviewed event bulletin.",
            Type = EventType.Webinar,
            Status = EventStatus.Approved,
            Level = EventLevel.Introductory,
            Nature = EventNature.International,
            AttendanceNature = AttendanceNature.Online,
            Language = "en", SeatsLimit = 0,               // unlimited
        }),

        // ---- two workshops on the same day: the -01 / -02 demonstration ----
        (30, 3, new Event
        {
            Title = "Workshop on Hydroacoustic Station Maintenance",
            Type = EventType.Workshop,
            Status = EventStatus.Approved,
            Level = EventLevel.Intermediate,
            Nature = EventNature.International,
            AttendanceNature = AttendanceNature.Physical,
            HostCountry = "PT", City = "Lisbon", Venue = "Instituto Hidrografico",
            Language = "en", SeatsLimit = 12,
        }),

        (30, 2, new Event
        {
            Title = "Workshop on Infrasound Array Calibration",
            Type = EventType.Workshop,
            Status = EventStatus.Approved,
            Level = EventLevel.Advanced,
            Nature = EventNature.International,
            AttendanceNature = AttendanceNature.HybridStreaming,
            HostCountry = "FR", City = "Bruyeres-le-Chatel", Venue = "CEA/DAM",
            Language = "fr", SeatsLimit = 20,
        }),

        (60, 5, new Event
        {
            Title = "Advanced Training on Noble Gas Systems",
            ShortTitle = "Noble Gas Systems",
            Objectives = "Operate, calibrate and troubleshoot SAUNA and SPALAX noble gas "
                       + "systems, and evaluate the resulting spectra against IDC categories.",
            Type = EventType.Training,
            Status = EventStatus.Approved,
            Level = EventLevel.Advanced,
            Nature = EventNature.International,
            AttendanceNature = AttendanceNature.Physical,
            HostCountry = "SE", City = "Stockholm", Venue = "FOI",
            HostInstitution = "Swedish Defence Research Agency",
            Language = "en", SeatsLimit = 15,
        }),

        (75, 2, new Event
        {
            Title = "Technical and Expert Meeting on Station Sustainment",
            Type = EventType.TechnicalMeeting,
            Status = EventStatus.Approved,
            Level = EventLevel.Other,
            Nature = EventNature.International,
            AttendanceNature = AttendanceNature.Hybrid,
            HostCountry = "AT", City = "Vienna", Venue = "Vienna International Centre",
            Language = "en", SeatsLimit = 40,
        }),

        // Draft and Tentative: both appear in the table, neither can be
        // enrolled in. The Angular Enrol button is disabled for them and
        // EventEndpoints.Enrol answers 409 if you send the request anyway.
        (90, 4, new Event
        {
            Title = "Symposium on Science and Technology 2027",
            ShortTitle = "SnT 2027",
            Type = EventType.Symposium,
            Status = EventStatus.Tentative,
            Level = EventLevel.Other,
            Nature = EventNature.International,
            AttendanceNature = AttendanceNature.Hybrid,
            HostCountry = "AT", City = "Vienna", Venue = "Hofburg Palace",
            Language = "en", SeatsLimit = 0,
        }),

        (120, 3, new Event
        {
            Title = "National Data Centre Capacity Building Seminar",
            Type = EventType.Seminar,
            Status = EventStatus.Draft,
            Level = EventLevel.Introductory,
            Nature = EventNature.National,
            AttendanceNature = AttendanceNature.Physical,
            HostCountry = "DZ", City = "Algiers", Venue = "CRAAG",
            HostInstitution = "Centre de Recherche en Astronomie, Astrophysique et Geophysique",
            Language = "fr", SeatsLimit = 22,
        }),
    ];
}
