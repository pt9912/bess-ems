using System.Security.Claims;
using System.Text.Encodings.Web;
using BatteryEms.Application.Persistence;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BatteryEms.Api.Auth;

// Custom AuthenticationHandler that turns 'Authorization: Bearer <token>'
// into a ClaimsPrincipal with NameIdentifier=<operator> and Role=<role>.
// Anything else (no header, wrong scheme, unknown token) returns
// AuthenticateResult.Fail so RequireAuthorization triggers the 401
// challenge — the audit entry is written by the OnChallenge events
// configured in Program.cs.
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by AddScheme<TOptions, THandler>() via reflection.")]
internal sealed class ApiTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private const string HeaderName = "Authorization";
    private const string BearerPrefix = "Bearer ";

    private readonly ApiTokenRegistry _registry;

    public ApiTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApiTokenRegistry registry)
        : base(options, logger, encoder)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values) || values.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var raw = values[0];
        if (string.IsNullOrWhiteSpace(raw) || !raw.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.Fail("Unsupported Authorization scheme."));
        }

        var token = raw.AsSpan(BearerPrefix.Length).Trim().ToString();
        if (token.Length == 0)
        {
            return Task.FromResult(AuthenticateResult.Fail("Empty bearer token."));
        }

        if (!_registry.TryResolve(token, out var entry))
        {
            return Task.FromResult(AuthenticateResult.Fail("Unknown bearer token."));
        }

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, entry.Operator),
                new Claim(ClaimTypes.Role, entry.Role),
            },
            AuthConstants.SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, AuthConstants.SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    // LH-API-007 mandates audit trails for accepted AND rejected operator
    // writes. Accepted/invalid entries are written from the endpoint where
    // the body is available; unauthorized/forbidden entries are written
    // here because the auth pipeline short-circuits before any endpoint
    // code runs. The path filter keeps the audit volume bounded to write
    // endpoints — read endpoints don't require auth in M1.
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (IsOperatorStopWrite(Context.Request))
        {
            await AppendAuditAsync(
                operatorId: AuthConstants.AnonymousOperator,
                outcome: AuthConstants.OutcomeUnauthorized,
                reason: "missing-or-invalid-credentials")
                .ConfigureAwait(false);
        }
        await base.HandleChallengeAsync(properties).ConfigureAwait(false);
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        if (IsOperatorStopWrite(Context.Request))
        {
            var operatorId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? AuthConstants.AnonymousOperator;
            await AppendAuditAsync(
                operatorId: operatorId,
                outcome: AuthConstants.OutcomeForbidden,
                reason: "insufficient-role")
                .ConfigureAwait(false);
        }
        await base.HandleForbiddenAsync(properties).ConfigureAwait(false);
    }

    private static bool IsOperatorStopWrite(HttpRequest request)
        => HttpMethods.IsPost(request.Method)
            && request.Path.Equals("/operator/stop", StringComparison.Ordinal);

    private async Task AppendAuditAsync(string operatorId, string outcome, string reason)
    {
        var auditLog = Context.RequestServices.GetRequiredService<IOperatorAuditLog>();
        var clock = Context.RequestServices.GetRequiredService<IClock>();
        var auditEvent = new AuditEvent(
            Timestamp: clock.UtcNow,
            Operator: operatorId,
            Action: AuthConstants.OperatorStopAction,
            TargetAssetId: null,
            Reason: reason,
            Outcome: outcome);
        await auditLog.AppendAsync(auditEvent, Context.RequestAborted).ConfigureAwait(false);
    }
}
