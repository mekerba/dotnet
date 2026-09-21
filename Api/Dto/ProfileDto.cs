using Remus.Mvc.Data;

namespace Remus.Mvc.Api.Dto;

// ============================================================================
// Api/Dto/  ==  Django REST Framework's serializers.py
//
// Models/ already holds VIEW MODELS (Django: forms.py) - shapes bound from an
// HTML <form> and rendered back into a .cshtml. This folder holds DTOs: shapes
// that cross the wire as JSON. Two folders because they answer to two different
// consumers, and the difference is visible in the code:
//
//   Models/ProfileEditViewModel   [Display(Name = "First name")]  <- a LABEL.
//                                 Meaningless to Angular; it renders its own.
//   Api/Dto/ProfileUpdateDto      no [Display] at all.
//
// In a smaller app you could serve JSON straight from the view model and skip
// this folder entirely. It is split here because the point of the exercise is
// to see where the seam is.
// ============================================================================

/// <summary>
/// What GET /api/profile returns. Read-only: nothing here is ever bound FROM a
/// request, so it is safe for it to expose IsValidated, Created and so on.
/// </summary>
/// <remarks>
/// The output half of a DRF serializer:
///
///     class ProfileSerializer(serializers.ModelSerializer):
///         email = serializers.EmailField(source="user.email", read_only=True)
///         class Meta:
///             model = Profile
///             fields = ["email", "title", "first_name", ...]
///
/// Note `fields = [...]` and not `"__all__"`, and note that this class lists
/// its properties by hand for the identical reason: serialising the entity
/// itself publishes every column you add to it later, forever, by accident.
///
/// ---------------------------------------------------------------------------
/// ENUMS ARRIVE AS STRINGS HERE, and no .ToString() is needed.
///
/// ProfileController.Summary had to write `Title = profile.Title?.ToString()`
/// because System.Text.Json serialises an enum as its ORDINAL by default - the
/// modal would have shown "0" instead of "Mr".
///
/// Program.cs now registers a JsonStringEnumConverter globally instead, so
/// `Title? Title` goes out as "Mr" on its own. One line of configuration
/// replaces a .ToString() on every enum in every endpoint you ever write.
/// ---------------------------------------------------------------------------
/// </remarks>
public sealed record ProfileDto
{
    // Lives on ApplicationUser, not Profile. Django: source="user.email".
    public required string Email { get; init; }
    public required bool EmailConfirmed { get; init; }

    public Title? Title { get; init; }
    public Gender? Gender { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }

    // DateOnly serialises as "1980-04-17" - System.Text.Json knows the type.
    // In TypeScript it arrives as a plain string, which is why the Angular
    // model types it `string | null` and not `Date`.
    public DateOnly? DateBirth { get; init; }
    public string? PlaceBirth { get; init; }

    public string? City { get; init; }
    public string? Country { get; init; }
    public string? Nationality { get; init; }
    public string? Position { get; init; }
    public string? Institution { get; init; }
    public string? Biography { get; init; }

    public required bool IsValidated { get; init; }
    public required DateTimeOffset Created { get; init; }
    public required DateTimeOffset LastUpdate { get; init; }

    // A computed property with no setter still SERIALISES - System.Text.Json
    // writes anything readable. Django: a SerializerMethodField, except you do
    // not have to declare it.
    public string FullName =>
        string.Join(" ", new[] { FirstName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>Flattens a user + profile pair, exactly as the view model does.</summary>
    public static ProfileDto FromEntity(ApplicationUser user, Profile profile) => new()
    {
        Email = user.Email ?? "",
        EmailConfirmed = user.EmailConfirmed,
        Title = profile.Title,
        Gender = profile.Gender,
        FirstName = profile.FirstName,
        LastName = profile.LastName,
        DateBirth = profile.DateBirth,
        PlaceBirth = profile.PlaceBirth,
        City = profile.City,
        Country = profile.Country,
        Nationality = profile.Nationality,
        Position = profile.Position,
        Institution = profile.Institution,
        Biography = profile.Biography,
        IsValidated = profile.IsValidated,
        Created = profile.Created,
        LastUpdate = profile.LastUpdate,
    };

    // ProfileImage is deliberately absent, same as on the MVC pages: the path
    // in that column points into the BLAZOR app's wwwroot. A shared database is
    // not a shared filesystem. See README.md.
}
