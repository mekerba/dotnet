using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Remus.Mvc.Authorization;
using Remus.Mvc.Data;

namespace Remus.Mvc.Commands;

// ============================================================================
// createsuperuser  —  Django: manage.py createsuperuser
//
// ASP.NET Core ships no equivalent. Django has manage.py, a second entry point
// into the same settings and the same ORM; .NET has exactly one entry point,
// Program.cs, whose job is to start a web server. So a management command here
// means branching on args BEFORE app.Run() and never starting the server.
//
// What we get for free by living inside Program.cs: the container is already
// built, so UserManager, RoleManager and ApplicationDbContext come out of DI
// configured exactly as the web app configures them. The password rules in
// Program.cs (8 chars, digit, upper, lower, symbol) are enforced here too,
// because it is the same UserManager the login page uses.
//
// Django's version prompts for username/email/password and writes one row with
// is_superuser = True. There is no is_superuser flag in ASP.NET Identity —
// "superuser" is not a concept, only roles are. So this creates a user and
// puts it in the Admin and SysMan roles, which is what the Blazor app's
// DbSeeder means by "admin".
//
//   dotnet run -- createsuperuser
//   dotnet run -- createsuperuser boss@remus.local 'Str0ng!Pass'
//   dotnet run -- createsuperuser boss@remus.local 'Str0ng!Pass' --roles Admin,SysMan
// ============================================================================
public static class CreateSuperUser
{
    /// <summary>The verb that selects this command on the command line.</summary>
    public const string Verb = "createsuperuser";

    /// <summary>What "superuser" means here, absent an is_superuser column.</summary>
    private static readonly string[] SuperuserRoles = [AppRoles.Admin, AppRoles.SysMan];

    /// <returns>A process exit code: 0 success, 1 refused, 2 bad usage.</returns>
    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        // --- parse ----------------------------------------------------------
        // args[0] is the verb itself.
        string[] roles = SuperuserRoles;
        var positional = new List<string>();

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h":
                    PrintUsage();
                    return 0;

                case "--roles" or "-r":
                    if (i + 1 >= args.Length)
                        return Fail("--roles needs a comma-separated list of role names.", 2);

                    roles = args[++i].Split(
                        ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    break;

                default:
                    if (args[i].StartsWith('-'))
                        return Fail($"Unknown option '{args[i]}'.", 2);

                    positional.Add(args[i]);
                    break;
            }
        }

        if (positional.Count > 2)
            return Fail("Too many arguments. Expected at most an email and a password.", 2);

        // Missing arguments are prompted for, the way Django's command does.
        // Passing the password as an argument is convenient for scripts and
        // bad for your shell history; prompting is the default for a reason.
        var email = positional.Count > 0 ? positional[0] : Prompt("Email");
        if (string.IsNullOrWhiteSpace(email))
            return Fail("An email address is required.", 2);

        string password;
        if (positional.Count > 1)
        {
            password = positional[1];
        }
        else
        {
            password = PromptPassword("Password");
            if (password != PromptPassword("Password (again)"))
                return Fail("The two passwords did not match.", 2);
        }

        if (string.IsNullOrEmpty(password))
            return Fail("A password is required.", 2);

        email = email.Trim();

        // --- resolve services -----------------------------------------------
        // A scope, because UserManager and ApplicationDbContext are registered
        // scoped — one per HTTP request in the web app, one per command here.
        // Resolving them from the root provider would throw.
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var db = sp.GetRequiredService<ApplicationDbContext>();

        // --- check the roles exist before touching anything -------------------
        // This project does not own the schema and does not create roles; the
        // Blazor app's DbSeeder does. A typo'd role name must fail loudly here
        // rather than produce an account that silently has fewer powers.
        foreach (var role in roles)
        {
            if (await roleManager.RoleExistsAsync(role)) continue;

            var known = await roleManager.Roles.Select(r => r.Name).OrderBy(n => n).ToListAsync();
            return Fail(
                $"No such role '{role}'. Roles in this database: {string.Join(", ", known)}", 1);
        }

        if (await userManager.FindByEmailAsync(email) is not null)
            return Fail($"A user with the email '{email}' already exists.", 1);

        // --- create -----------------------------------------------------------
        // One transaction over all three writes. Django gets this from
        // ATOMIC_REQUESTS or @transaction.atomic; EF Core needs it spelled out.
        // Without it a failed role assignment leaves a user with no roles and
        // no way to notice.
        await using var tx = await db.Database.BeginTransactionAsync();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,

            // Program.cs sets RequireConfirmedAccount = true, so without this
            // the account is created and cannot sign in. A superuser made at a
            // terminal by the person who owns the database does not need to
            // prove they own the mailbox.
            EmailConfirmed = true,
        };

        var created = await userManager.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            await tx.RollbackAsync();
            return Fail("Could not create the user:" + Bullets(created.Errors), 1);
        }

        // Every user has exactly one profile — the invariant ApplicationUser's
        // Profile property documents. Created here explicitly because EF Core
        // has no post_save signal to hang it off.
        db.Profiles.Add(new Profile { UserId = user.Id, IsValidated = true });
        await db.SaveChangesAsync();

        // BasicUser on top of whatever was asked for: it is the role every
        // account gets at registration, and an admin missing it would be an
        // odd special case for any check written against it.
        var toAssign = roles.Contains(AppRoles.BasicUser)
            ? roles
            : [.. roles, AppRoles.BasicUser];

        var assigned = await userManager.AddToRolesAsync(user, toAssign);
        if (!assigned.Succeeded)
        {
            await tx.RollbackAsync();
            return Fail("Could not assign roles:" + Bullets(assigned.Errors), 1);
        }

        await tx.CommitAsync();

        Console.WriteLine();
        Console.WriteLine($"  Created {email}");
        Console.WriteLine($"  Roles:   {string.Join(", ", toAssign.Order())}");
        Console.WriteLine($"  Profile: created, IsValidated = true");
        Console.WriteLine();
        Console.WriteLine("  Sign in at /Account/Login.");
        Console.WriteLine();
        return 0;
    }

    // ------------------------------------------------------------------------
    // Console helpers
    // ------------------------------------------------------------------------

    private static string Prompt(string label)
    {
        Console.Write($"{label}: ");
        return Console.ReadLine() ?? string.Empty;
    }

    private static string PromptPassword(string label)
    {
        Console.Write($"{label}: ");

        // Piped input (a script, a test) has no terminal to read keys from.
        if (Console.IsInputRedirected)
            return Console.ReadLine() ?? string.Empty;

        var buffer = new System.Text.StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    return buffer.ToString();

                case ConsoleKey.Backspace when buffer.Length > 0:
                    buffer.Length--;
                    break;

                case ConsoleKey.Backspace:
                    break;

                default:
                    if (!char.IsControl(key.KeyChar))
                        buffer.Append(key.KeyChar);
                    break;
            }
        }
    }

    private static string Bullets(IEnumerable<IdentityError> errors) =>
        string.Concat(errors.Select(e => $"{Environment.NewLine}  - {e.Description}"));

    private static int Fail(string message, int exitCode)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine($"  {message}");
        Console.Error.WriteLine();
        return exitCode;
    }

    private static void PrintUsage()
    {
        Console.WriteLine($"""

              Usage: dotnet run -- {Verb} [<email>] [<password>] [--roles <a,b,c>]

              Creates a user, its profile, and its role assignments. Omitted
              arguments are prompted for. Defaults to the roles {string.Join(", ", SuperuserRoles)}
              plus {AppRoles.BasicUser}.

              Known roles: {string.Join(", ", AppRoles.All)}

            """);
    }
}
