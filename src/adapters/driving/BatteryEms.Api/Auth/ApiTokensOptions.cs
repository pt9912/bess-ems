namespace BatteryEms.Api.Auth;

// API-token configuration bound from IConfiguration ("ApiTokens" section).
// Each entry maps an opaque bearer token to an operator identity and a
// single role — M1 only ships one role (operator) per RM-OPEN-04.
//
// Tokens are compared as exact strings; rotation is "edit config + restart".
// Hashing/expiry would land via a follow-up ADR (RM-OPEN-04 leaves the door
// open for OIDC / mTLS post-M1).
public sealed class ApiTokensOptions
{
    public const string SectionName = "ApiTokens";

    public IList<ApiTokenEntry> Tokens { get; } = new List<ApiTokenEntry>();
}

public sealed class ApiTokenEntry
{
    public string Token { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
