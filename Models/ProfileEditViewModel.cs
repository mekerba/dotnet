using System.ComponentModel.DataAnnotations;
using Remus.Mvc.Data;

namespace Remus.Mvc.Models;

/// <summary>
/// What the profile edit form binds on POST.
/// </summary>
/// <remarks>
/// Django:
///
///     class ProfileForm(forms.ModelForm):
///         class Meta:
///             model = Profile
///             fields = ["title", "gender", "first_name", ...]
///
/// ---------------------------------------------------------------------------
/// THIS is where "never bind to your entity" matters.
///
/// If the POST action took `Profile` instead of this class, MVC's model binder
/// would happily populate ANY public settable property from the request body —
/// including IsValidated, UserId and Created. A crafted form field
/// `IsValidated=true` would self-validate the account. That is over-posting,
/// and it is the exact same hole as Django's `fields = "__all__"`.
///
/// This class simply has no such properties, so no request can set them. The
/// MVC alternatives — [Bind("FirstName,LastName")] on the parameter, or
/// TryUpdateModelAsync with an include list — work but are easy to forget.
/// A purpose-built view model cannot be forgotten.
/// ---------------------------------------------------------------------------
///
/// Note that ProfileImage is absent too: uploads belong to a separate action
/// with its own validation, never to ordinary form binding.
/// </remarks>
public class ProfileEditViewModel
{
    [Display(Name = "Title")]
    public Title? Title { get; set; }

    [Display(Name = "Gender")]
    public Gender? Gender { get; set; }

    [Required(ErrorMessage = "First name is required.")]
    [StringLength(250, ErrorMessage = "{0} cannot exceed {1} characters.")]
    [Display(Name = "First name")]
    public string? FirstName { get; set; }

    [Required(ErrorMessage = "Last name is required.")]
    [StringLength(250, ErrorMessage = "{0} cannot exceed {1} characters.")]
    [Display(Name = "Last name")]
    public string? LastName { get; set; }

    // [DataType(Date)] makes the editor render <input type="date">.
    // Django: forms.DateField with widget=forms.DateInput(attrs={"type": "date"}).
    [DataType(DataType.Date)]
    [Display(Name = "Date of birth")]
    public DateOnly? DateBirth { get; set; }

    [StringLength(100)]
    [Display(Name = "Place of birth")]
    public string? PlaceBirth { get; set; }

    [StringLength(250)]
    [Display(Name = "City")]
    public string? City { get; set; }

    // Django: RegexValidator on a CharField(max_length=2).
    [StringLength(2, MinimumLength = 2, ErrorMessage = "Use a 2-letter country code, e.g. DZ.")]
    [RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Use a 2-letter country code, e.g. DZ.")]
    [Display(Name = "Country")]
    public string? Country { get; set; }

    [StringLength(2, MinimumLength = 2, ErrorMessage = "Use a 2-letter country code, e.g. DZ.")]
    [RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Use a 2-letter country code, e.g. DZ.")]
    [Display(Name = "Nationality")]
    public string? Nationality { get; set; }

    [StringLength(250)]
    [Display(Name = "Position")]
    public string? Position { get; set; }

    [StringLength(250)]
    [Display(Name = "Institution")]
    public string? Institution { get; set; }

    [StringLength(4000, ErrorMessage = "{0} cannot exceed {1} characters.")]
    [DataType(DataType.MultilineText)]   // Django: widget=forms.Textarea
    [Display(Name = "Biography")]
    public string? Biography { get; set; }

    /// <summary>Entity -> form. Django: ProfileForm(instance=profile)</summary>
    public static ProfileEditViewModel FromEntity(Profile p) => new()
    {
        Title = p.Title,
        Gender = p.Gender,
        FirstName = p.FirstName,
        LastName = p.LastName,
        DateBirth = p.DateBirth,
        PlaceBirth = p.PlaceBirth,
        City = p.City,
        Country = p.Country,
        Nationality = p.Nationality,
        Position = p.Position,
        Institution = p.Institution,
        Biography = p.Biography,
    };

    /// <summary>
    /// Form -> entity. Mutates only; the caller decides when to SaveChanges.
    /// </summary>
    /// <remarks>
    /// Django's form.save() both mutates and writes. Splitting them is what
    /// lets the controller do several things in one transaction.
    /// </remarks>
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
