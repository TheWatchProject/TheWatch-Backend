namespace TheWatch.Security;

/// <summary>
/// Standard authorization claim types and role definitions across TheWatch ecosystem.
/// </summary>
public static class SecurityClaims
{
    public const string RoleClaim = "role";
    public const string UserIdClaim = "sub";
    public const string OrganizationIdClaim = "org_id";
    public const string JurisdictionClaim = "jurisdiction";
    public const string ClearanceLevelClaim = "clearance_level";

    public static class Roles
    {
        public const string Dispatcher = "Dispatcher";
        public const string FirstResponder = "FirstResponder";
        public const string IncidentCommander = "IncidentCommander";
        public const string Admin = "Administrator";
        public const string Viewer = "Viewer";
        public const string MeshNode = "MeshNode";
    }

    public static class Policies
    {
        public const string RequireDispatcher = "RequireDispatcherPolicy";
        public const string RequireResponder = "RequireResponderPolicy";
        public const string RequireCommander = "RequireCommanderPolicy";
        public const string RequireAdmin = "RequireAdminPolicy";
    }
}
