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
/// Marks an entity that carries creation/modification timestamps.
/// </summary>
/// <remarks>
/// Django:  created     = models.DateTimeField(auto_now_add=True)
///          last_update = models.DateTimeField(auto_now=True)
///
/// EF Core has no auto_now / auto_now_add. There is no field-level hook that
/// fires on save. The idiomatic replacement is to override SaveChangesAsync on
/// the DbContext and stamp anything implementing this interface - see
/// ApplicationDbContext.SaveChangesAsync.
///
/// Why an interface rather than checking `is Profile`: the stamping code stays
/// written once, and any future entity opts in by adding ": IAuditedEntity".
/// That is the closest .NET gets to Django's "declare it on the field".
/// </remarks>
public interface IAuditedEntity
{
    DateTimeOffset Created { get; set; }
    DateTimeOffset LastUpdate { get; set; }
}
