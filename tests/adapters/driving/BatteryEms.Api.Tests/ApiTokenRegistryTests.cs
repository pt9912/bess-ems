using BatteryEms.Api.Auth;
using Microsoft.Extensions.Options;
using Xunit;

namespace BatteryEms.Api.Tests;

public sealed class ApiTokenRegistryTests
{
    [Fact]
    public void Empty_token_list_resolves_no_tokens()
    {
        var registry = Build();
        Assert.False(registry.TryResolve("anything", out _));
    }

    [Fact]
    public void TryResolve_returns_entry_for_known_token()
    {
        var registry = Build(("tok", "op", "operator"));
        Assert.True(registry.TryResolve("tok", out var entry));
        Assert.Equal("op", entry.Operator);
        Assert.Equal("operator", entry.Role);
    }

    [Fact]
    public void TryResolve_is_case_sensitive()
    {
        var registry = Build(("Tok", "op", "operator"));
        Assert.False(registry.TryResolve("tok", out _));
    }

    [Fact]
    public void Constructor_throws_for_blank_token()
    {
        Assert.Throws<InvalidOperationException>(() => Build(("", "op", "operator")));
    }

    [Fact]
    public void Constructor_throws_for_blank_operator()
    {
        Assert.Throws<InvalidOperationException>(() => Build(("tok", "", "operator")));
    }

    [Fact]
    public void Constructor_throws_for_blank_role()
    {
        Assert.Throws<InvalidOperationException>(() => Build(("tok", "op", "")));
    }

    [Fact]
    public void Constructor_throws_for_duplicate_token()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Build(("dup", "a", "operator"), ("dup", "b", "operator")));
    }

    private static ApiTokenRegistry Build(params (string Token, string Operator, string Role)[] entries)
    {
        var options = new ApiTokensOptions();
        foreach (var (token, op, role) in entries)
        {
            options.Tokens.Add(new ApiTokenEntry { Token = token, Operator = op, Role = role });
        }
        return new ApiTokenRegistry(Options.Create(options));
    }
}
