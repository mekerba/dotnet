using System.ComponentModel.DataAnnotations;

namespace Remus.Mvc.Models;

/// <summary>
/// What the login form posts.
/// </summary>
/// <remarks>
/// Django:
///
///     class LoginForm(AuthenticationForm):
///         ...
///
/// ---------------------------------------------------------------------------
/// "VIEW MODEL" IS THE MVC NAME FOR THIS. It is not a database model.
///
/// MVC's "M" covers two different things that Django keeps in one class:
///   * the DOMAIN model  -> Data/Profile.cs, Data/ApplicationUser.cs
///   * the VIEW model    -> this folder
///
/// A view model exists to describe exactly one screen: what it displays and
/// what it accepts back. Django's ModelForm derives that from the model; MVC
/// makes you write it, and in exchange nothing can be bound that you did not
/// declare.
/// ---------------------------------------------------------------------------
///
/// [Display(Name = ...)] is what asp-for renders into the &lt;label&gt;, and what
/// error messages substitute for {0}. Django: the field's `label` kwarg.
/// </remarks>
public class LoginViewModel
{
    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress(ErrorMessage = "That does not look like an email address.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Enter your password.")]
    [DataType(DataType.Password)]   // Django: widget=forms.PasswordInput
    [Display(Name = "Password")]
    public string Password { get; set; } = "";

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }
}
