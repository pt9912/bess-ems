using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BatteryEms.Api.Auth;

// Bundles AuthN/AuthZ wiring so Program.BuildApp's class-coupling stays
// under the CA1506 threshold. Production and tests share this path; tests
// override the in-memory configuration to seed deterministic tokens.
public static class AuthRegistration
{
    public static IServiceCollection AddApiTokenAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ApiTokensOptions>(
            configuration.GetSection(ApiTokensOptions.SectionName));
        services.AddSingleton<ApiTokenRegistry>();
        services
            .AddAuthentication(AuthConstants.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(
                AuthConstants.SchemeName, _ => { });
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthConstants.OperatorPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(AuthConstants.OperatorRole));
        return services;
    }
}
