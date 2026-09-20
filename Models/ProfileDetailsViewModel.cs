using Remus.Mvc.Data;

namespace Remus.Mvc.Models;

/// <summary>
/// Everything the read-only profile page shows.
/// </summary>
/// <remarks>
/// Django would just do:
///
///     return render(request, "users/profile.html", {"profile": request.user.profile})
///
/// ...and let the template walk `profile.user.email`. That is safe in Django
/// because a template can only read.
///
/// Passing the entity straight to an MVC view is equally safe for the same
/// reason — views only render. The rule "never expose your entity" applies to
/// MODEL BINDING on POST, not to rendering. See ProfileEditViewModel.
///
/// A view model is used here anyway, for two ordinary reasons:
///   1. the page needs Email, which lives on ApplicationUser, not Profile — so
///      something has to flatten the two;
///   2. it keeps the view from lazily reaching through navigations that were
///      never Included, which in EF yields null rather than a query.
/// </remarks>
public class ProfileDetailsViewModel
{
    public required string Email { get; init; }
    public required bool EmailConfirmed { get; init; }

    public Title? Title { get; init; }
    public Gender? Gender { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public DateOnly? DateBirth { get; init; }
    public string? PlaceBirth { get; init; }
    public string? City { get; init; }
    public string? Country { get; init; }
    public string? Nationality { get; init; }
    public string? Position { get; init; }
    public string? Institution { get; init; }
    public string? Biography { get; init; }
    public string? ProfileImage { get; init; }
    public required bool IsValidated { get; init; }
    public required DateTimeOffset Created { get; init; }
    public required DateTimeOffset LastUpdate { get; init; }

    public string FullName =>
        string.Join(" ", new[] { FirstName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public string DisplayName => FullName is { Length: > 0 } ? FullName : Email;

    public bool IsIncomplete =>
        string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(LastName);

    /// <summary>Flattens a user + profile pair. Django: the context dict.</summary>
    public static ProfileDetailsViewModel FromEntity(ApplicationUser user, Profile profile) => new()
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
        ProfileImage = profile.ProfileImage,
        IsValidated = profile.IsValidated,
        Created = profile.Created,
        LastUpdate = profile.LastUpdate,
    };
}
