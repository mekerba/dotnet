using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Remus.Mvc.Models;

namespace Remus.Mvc.Controllers;

/// <summary>
/// The landing page.
/// </summary>
/// <remarks>
/// Anonymous — there is no [Authorize] here, and no global authorization
/// filter in Program.cs, so anything not explicitly protected is public.
///
/// Django is the same by default: a view without @login_required is open.
/// (Both frameworks can invert this — Django with a middleware such as
/// django-login-required, MVC with a global AuthorizeFilter. Neither does by
/// default, which is worth remembering when you add a controller.)
///
/// HomeController is the route template's default controller and Index its
/// default action, so "/" reaches this method with nothing written down:
///
///     pattern: "{controller=Home}/{action=Index}/{id?}"
///
/// Django would need  path("", views.index, name="home").
/// </remarks>
public class HomeController(ILogger<HomeController> logger) : Controller
{
    public IActionResult Index() => View();

    // [ResponseCache(NoStore = true)] — Django: @never_cache
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        logger.LogWarning("Error page shown for request {RequestId}.", requestId);
        return View(new ErrorViewModel { RequestId = requestId });
    }
}
