namespace BatteryEms.Api.Auth;

// Single source of truth for the authentication scheme and authorisation
// policy names so endpoints and the auth handler can't drift apart.
public static class AuthConstants
{
    public const string SchemeName = "ApiToken";

    // LH-API-007: write endpoints require the operator role. M1 has only
    // this one role; finer-grained RBAC would land via a follow-up ADR.
    public const string OperatorPolicy = "operator";
    public const string OperatorRole = "operator";

    // Action label written into the audit log for every /operator/stop
    // attempt. Kept stable across outcomes so audit queries can group by
    // action regardless of accept/reject.
    public const string OperatorStopAction = "operator-stop";

    // Outcome labels persisted on the AuditEvent (LH-API-007:
    // "angenommene und abgelehnte schreibende Operator-Aktionen").
    public const string OutcomeAccepted = "accepted";
    public const string OutcomeUnauthorized = "unauthorized";
    public const string OutcomeForbidden = "forbidden";
    public const string OutcomeInvalid = "invalid";

    // Identity placeholder for audit entries written before any token
    // could be resolved (no Authorization header / unknown token).
    public const string AnonymousOperator = "anonymous";
}
