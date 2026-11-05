using Maran.Modules.Identity.Commands.CreateApiKey;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Commands.CreateApiKey;

/// <summary>Behavioural contract of <see cref="CreateApiKeyCommandValidator"/>.</summary>
public sealed class CreateApiKeyCommandValidatorTests
{
    private readonly CreateApiKeyCommandValidator _validator = new();

    /// <summary>Builds a command that is valid until a test replaces one input.</summary>
    private static CreateApiKeyCommand Command(
        string name = "CI",
        IReadOnlyList<string>? scopes = null,
        IReadOnlyList<string>? cidrs = null)
    {
        return new CreateApiKeyCommand(name, scopes ?? [ApiKeyScopes.SelfRead], cidrs);
    }

    /// <summary>A name, a known scope and valid ranges are accepted, the inverse control for every refusal below.</summary>
    [Fact]
    public void A_name_a_known_scope_and_valid_ranges_are_accepted()
    {
        Assert.True(_validator.Validate(Command()).IsValid);
        Assert.True(_validator.Validate(Command(cidrs: ["203.0.113.0/24", "2001:db8::/32", "198.51.100.9"])).IsValid);
        Assert.True(_validator.Validate(Command(scopes: [ApiKeyScopes.SelfRead, ApiKeyScopes.ServerRead])).IsValid);
        Assert.True(_validator.Validate(Command(name: new string('n', ApiKeyLimits.MaxNameLength))).IsValid);
    }

    /// <summary>A blank, overlong or control-character name is refused with the name code.</summary>
    /// <param name="name">The name sent.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("line\nbreak")]
    public void A_blank_or_control_character_name_is_refused(string name)
    {
        Assert.Equal("ApiKeyNameInvalid", Assert.Single(_validator.Validate(Command(name: name)).Errors).ErrorCode);
    }

    /// <summary>A name one character over the limit is refused.</summary>
    [Fact]
    public void A_name_over_the_limit_is_refused()
    {
        var result = _validator.Validate(Command(name: new string('n', ApiKeyLimits.MaxNameLength + 1)));

        Assert.Equal("ApiKeyNameInvalid", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>No scope, an unknown scope and a null scope are each refused with the scope code.</summary>
    [Fact]
    public void No_scope_an_unknown_scope_and_a_null_scope_are_refused()
    {
        Assert.Equal("ApiKeyScopeUnknown", Assert.Single(_validator.Validate(Command(scopes: [])).Errors).ErrorCode);
        Assert.Equal("ApiKeyScopeUnknown", Assert.Single(_validator.Validate(Command(scopes: ["accounts.read"])).Errors).ErrorCode);
        Assert.Equal("ApiKeyScopeUnknown", Assert.Single(_validator.Validate(Command(scopes: ["SELF.READ"])).Errors).ErrorCode);
        Assert.Equal("ApiKeyScopeUnknown", Assert.Single(_validator.Validate(Command(scopes: [ApiKeyScopes.SelfRead, null!])).Errors).ErrorCode);
    }

    /// <summary>A range that is malformed, has host bits set or is a zero-length prefix is refused with the range code.</summary>
    /// <param name="cidr">The range sent.</param>
    [Theory]
    [InlineData("not-an-address")]
    [InlineData("203.0.113.5/24")]
    [InlineData("203.0.113.0/33")]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    [InlineData("")]
    public void A_malformed_host_bit_or_zero_length_range_is_refused(string cidr)
    {
        var result = _validator.Validate(Command(cidrs: [cidr]));

        Assert.Equal("ApiKeyCidrInvalid", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>An empty range list is a malformed request, refused with the range code, while null (not bound) is accepted.</summary>
    [Fact]
    public void An_empty_range_list_is_refused_and_a_null_one_is_accepted()
    {
        Assert.Equal("ApiKeyCidrInvalid", Assert.Single(_validator.Validate(Command(cidrs: [])).Errors).ErrorCode);
        Assert.True(_validator.Validate(Command(cidrs: null)).IsValid);
    }

    /// <summary>One bad range among good ones refuses the whole list.</summary>
    [Fact]
    public void One_bad_range_among_good_ones_refuses_the_list()
    {
        var result = _validator.Validate(Command(cidrs: ["203.0.113.0/24", "0.0.0.0/0"]));

        Assert.Equal("ApiKeyCidrInvalid", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>The range list may hold exactly the limit and no more.</summary>
    [Fact]
    public void The_range_list_may_hold_exactly_the_limit_and_no_more()
    {
        var atLimit = Enumerable.Range(0, ApiKeyLimits.MaxCidrs).Select(i => { return $"10.0.{i}.0/24"; }).ToList();
        var overLimit = Enumerable.Range(0, ApiKeyLimits.MaxCidrs + 1).Select(i => { return $"10.0.{i}.0/24"; }).ToList();

        Assert.True(_validator.Validate(Command(cidrs: atLimit)).IsValid);
        Assert.Equal("ApiKeyCidrInvalid", Assert.Single(_validator.Validate(Command(cidrs: overLimit)).Errors).ErrorCode);
    }
}
