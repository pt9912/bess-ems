using Microsoft.Extensions.Options;

namespace BatteryEms.Api.Auth;

// Read-only view over ApiTokensOptions used by the authentication handler.
// The registry validates the configuration eagerly at construction so a
// malformed token list (blank token, blank operator, duplicate token)
// fails the host startup instead of silently letting requests through.
public sealed class ApiTokenRegistry
{
    private readonly Dictionary<string, ApiTokenEntry> _byToken;

    public ApiTokenRegistry(IOptions<ApiTokensOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _byToken = Build(options.Value);
    }

    public bool TryResolve(string token, out ApiTokenEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return _byToken.TryGetValue(token, out entry!);
    }

    private static Dictionary<string, ApiTokenEntry> Build(ApiTokensOptions options)
    {
        var byToken = new Dictionary<string, ApiTokenEntry>(StringComparer.Ordinal);
        foreach (var entry in options.Tokens)
        {
            if (string.IsNullOrWhiteSpace(entry.Token))
            {
                throw new InvalidOperationException("ApiTokens entry is missing 'token'.");
            }
            if (string.IsNullOrWhiteSpace(entry.Operator))
            {
                throw new InvalidOperationException(
                    $"ApiTokens entry for token '{Mask(entry.Token)}' is missing 'operator'.");
            }
            if (string.IsNullOrWhiteSpace(entry.Role))
            {
                throw new InvalidOperationException(
                    $"ApiTokens entry for operator '{entry.Operator}' is missing 'role'.");
            }
            if (!byToken.TryAdd(entry.Token, entry))
            {
                throw new InvalidOperationException(
                    $"ApiTokens contains duplicate token (operator '{entry.Operator}').");
            }
        }
        return byToken;
    }

    private static string Mask(string token)
        => token.Length <= 4 ? "***" : string.Concat(token.AsSpan(0, 2), "***");
}
