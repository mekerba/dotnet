using System.ComponentModel.DataAnnotations;
using Remus.Mvc.Data;

namespace Remus.Mvc.Api.Dto;

/// <summary>
/// What PUT /api/profile accepts. The input half of the serializer.
/// </summary>
/// <remarks>
/// ---------------------------------------------------------------------------
/// THE OVER-POSTING RULE APPLIES HERE TOO, AND HARDER.
///
/// ProfileEditViewModel's remarks explain why the HTML form never binds to
/// `Profile` directly: the binder would happily set IsValidated from a crafted
/// form field. A JSON body is no different - it is the same model binder, fed
/// from a different place. If this endpoint took `Profile`, then
///
///     PUT /api/profile  {"isValidated": true}
///
/// would self-validate the account. Django's `fields = "__all__"`, again.
///
/// So this class lists exactly the writable fields and nothing else. It is
/// near-identical to ProfileEditViewModel on purpose: same rules, different
/// consumer. The [Display] attributes are gone because a label is a rendering
/// concern and Angular writes its own.
/// ---------------------------------------------------------------------------
///
/// VALIDATION RUNS WITHOUT A LINE OF CODE IN THE ENDPOINT.
///
/// Program.cs calls builder.Services.AddValidation() - new in .NET 10. Minimal
/// APIs then check these DataAnnotations before the handler runs and, on
/// failure, short-circuit with 400 and an RFC 9457 ProblemDetails body:
///
///     {"title":"One or more validation errors occurred.",
///      "errors":{"FirstName":["First name is required."]}}
///
/// There is no ModelState.IsValid to remember, and forgetting the check is not
/// possible. DRF's serializer.is_valid(raise_exception=True), done for you.
/// The Angular form reads `errors` straight out of that body and paints the
/// messages next to the right fields.
/// </remarks>
public sealed record ProfileUpdateDto
{
    public Title? Title { get; init; }
    public Gender? Gender { get; init; }

    [Required(ErrorMessage = "First name is required.")]
    [StringLength(250, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? FirstName { get; init; }

    [Required(ErrorMessage = "Last name is required.")]
    [StringLength(250, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? LastName { get; init; }

    public DateOnly? DateBirth { get; init; }

    [StringLength(100)]
    public string? PlaceBirth { get; init; }

    [StringLength(250)]
    public string? City { get; init; }

    [StringLength(2, MinimumLength = 2, ErrorMessage = "Use a 2-letter country code, e.g. DZ.")]
    [RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Use a 2-letter country code, e.g. DZ.")]
    public string? Country { get; init; }

    [StringLength(2, MinimumLength = 2, ErrorMessage = "Use a 2-letter country code, e.g. DZ.")]
    [RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Use a 2-letter country code, e.g. DZ.")]
    public string? Nationality { get; init; }

    [StringLength(250)]
    public string? Position { get; init; }

    [StringLength(250)]
    public string? Institution { get; init; }

    [StringLength(4000, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? Biography { get; init; }

    /// <summary>
    /// DTO -> entity. Mutates only; the endpoint decides when to SaveChanges.
    /// Identical to ProfileEditViewModel.ApplyTo, for the same reasons.
    /// </summary>
    public void ApplyTo(Profile p)
    {
        p.Title = Title;
        p.Gender = Gender;
        p.FirstName = FirstName?.Trim();
        p.LastName = LastName?.Trim();
        p.DateBirth = DateBirth;
        p.PlaceBirth = PlaceBirth?.Trim();
        p.City = City?.Trim();
        p.Country = Country?.Trim().ToUpperInvariant();
        p.Nationality = Nationality?.Trim().ToUpperInvariant();
        p.Position = Position?.Trim();
        p.Institution = Institution?.Trim();
        p.Biography = Biography?.Trim();
    }
}
