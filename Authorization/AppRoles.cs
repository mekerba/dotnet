namespace Remus.Mvc.Authorization;

// ============================================================================
// The role names, as constants.
//
// Django: Group names. There, a group is a row you create in the admin and
// refer to by a string — Django gives you no compile-time help at all, which
// is why typo'd group names in a permission check are a classic Django bug.
//
// The same bug exists here: [Authorize(Roles = "Admn")] compiles fine and
// silently denies everyone. Constants are the only mitigation, so every role
// name in this project comes from here and never from a literal.
//
// These MUST match the Blazor app's Remus.Web.Authorization.AppRoles. The two
// apps share one AspNetRoles table; this project never creates roles, it only
// reads them. Adding a role means adding it there and re-running that app.
// ============================================================================
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string SysMan = "SysMan";
    public const string CbtOperator = "CbtOperator";
    public const string OsiOperator = "OsiOperator";
    public const string ScientificOperator = "ScientificOperator";
    public const string BasicUser = "BasicUser";

    public static readonly string[] All =
    [
        Admin, SysMan, CbtOperator, OsiOperator, ScientificOperator, BasicUser,
    ];
}
