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
/// Maps <see cref="Profile"/> to the database.
/// </summary>
/// <remarks>
/// This whole file is the equivalent of Django's field keyword arguments plus
/// the model's inner `class Meta`. Django puts mapping and domain in the same
/// class; EF lets you separate them, and IEntityTypeConfiguration&lt;T&gt; is the
/// conventional place. The alternative is data annotations ([MaxLength(250)],
/// [Table("...")]) on the entity itself, which reads more like Django but
/// cannot express relationships properly.
///
/// EF discovers this class automatically - see ApplicationDbContext's call to
/// ApplyConfigurationsFromAssembly. That is the nearest thing .NET has to
/// Django's app registry autodiscovery, and unlike Django it is opt-in: one
/// line in OnModelCreating, or the configuration is silently ignored.
/// </remarks>
public class ProfileConfiguration : IEntityTypeConfiguration<Profile>
{
    public void Configure(EntityTypeBuilder<Profile> builder)
    {
        // Django: class Meta: db_table = "users_profile"
        // Without this, EF's convention names the table after the DbSet property
        // ("Profiles"). We name it explicitly to sit alongside the AspNet* tables
        // with an obvious origin.
        builder.ToTable("Profiles");

        // ===================================================================
        // THE ONE-TO-ONE. This is the heart of the file.
        //
        // Django:
        //     user = models.OneToOneField(
        //         settings.AUTH_USER_MODEL,
        //         on_delete=models.CASCADE,
        //         related_name="profile",
        //     )
        //
        // Read the EF version below against that, line for line:
        //
        //   HasOne(p => p.User)            -> the target of the relationship
        //   WithOne(u => u.Profile)        -> related_name="profile"
        //   HasForeignKey<Profile>(...)    -> which side holds the FK column.
        //                                     The generic argument is what makes
        //                                     this one-to-one: EF puts a UNIQUE
        //                                     index on Profile.UserId, exactly
        //                                     like users_profile_user_id_key.
        //                                     Use WithMany() instead and you get
        //                                     a plain FK with no uniqueness.
        //   OnDelete(Cascade)              -> on_delete=models.CASCADE
        //
        // OnDelete is worth a pause. Django enforces CASCADE *in Python*: it
        // fetches the dependent rows and deletes them itself, so post_delete
        // signals fire. EF's DeleteBehavior.Cascade is emitted as an ON DELETE
        // CASCADE clause in the actual foreign key constraint, so the database
        // enforces it and the application never sees the rows. Faster, and it
        // works even when rows are deleted by hand in psql - but no hooks run.
        // ===================================================================
        builder.HasOne(p => p.User)
               .WithOne(u => u.Profile)
               .HasForeignKey<Profile>(p => p.UserId)
               .OnDelete(DeleteBehavior.Cascade);

        // Django: CharField(max_length=250, null=True)
        // EF: HasMaxLength -> varchar(250). Without it, Npgsql maps string to
        // `text` (unbounded), which is a perfectly good Postgres type but means
        // no length validation reaches the database at all.
        builder.Property(p => p.FirstName).HasMaxLength(250);
        builder.Property(p => p.LastName).HasMaxLength(250);
        builder.Property(p => p.PlaceBirth).HasMaxLength(100);
        builder.Property(p => p.City).HasMaxLength(250);
        builder.Property(p => p.Position).HasMaxLength(250);
        builder.Property(p => p.Institution).HasMaxLength(250);
        builder.Property(p => p.ProfileImage).HasMaxLength(255);

        // ISO 3166-1 alpha-2, matching remus_local's varchar(2).
        builder.Property(p => p.Country).HasMaxLength(2);
        builder.Property(p => p.Nationality).HasMaxLength(2);

        // Store the enum name, not its ordinal - see the comment in ProfileEnums.cs.
        // Result: a `title` column holding 'Mr'/'Dr', readable in psql and stable
        // if someone reorders the enum. This is what Django's TextChoices does.
        builder.Property(p => p.Title).HasConversion<string>().HasMaxLength(10);
        builder.Property(p => p.Gender).HasConversion<string>().HasMaxLength(20);

        // Biography stays unbounded `text` - Django's TextField.

        // Django: class Meta: indexes = [models.Index(fields=["date_birth"], ...)]
        // remus_local has users_profile_dob_idx and users_profile_lname_dob_idx.
        builder.HasIndex(p => p.DateBirth)
               .HasDatabaseName("ix_profiles_date_birth");

        builder.HasIndex(p => new { p.LastName, p.DateBirth })
               .HasDatabaseName("ix_profiles_lastname_datebirth");
    }
}
