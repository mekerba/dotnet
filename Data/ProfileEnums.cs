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
// Django:  class Title(models.TextChoices):
//              MR = "Mr", _("Mr")
//              DR = "Dr", _("Dr")
//          title = models.CharField(max_length=10, choices=Title.choices, null=True)
//
// .NET:    a plain C# enum. Two things differ from Django's TextChoices:
//
//   1. Django validates choices at the *form/model* layer (full_clean), not in
//      the database - the column is just a varchar. A C# enum is a compile-time
//      type: you literally cannot assign an invalid value, so there is no
//      "choices validation" step to forget.
//
//   2. Django stores the value side of the tuple ("Mr"). EF Core stores the
//      underlying int (0, 1, 2...) *unless* told otherwise. We tell it
//      otherwise in ProfileConfiguration via .HasConversion<string>(), so the
//      column holds "Mr" exactly like users_profile.title does in remus_local.
//      Storing ints would be smaller but unreadable in psql, and renumbering
//      the enum would silently repoint existing rows.
// ---------------------------------------------------------------------------
public enum Title
{
    Mr,
    Mrs,
    Ms,
    Dr,
    Prof,
}

// Django: gender = models.CharField(max_length=20, choices=..., null=True)
// remus_local stores the label ("Male"), so we do the same.
public enum Gender
{
    Male,
    Female,
    Other,
    PreferNotToSay,
}
