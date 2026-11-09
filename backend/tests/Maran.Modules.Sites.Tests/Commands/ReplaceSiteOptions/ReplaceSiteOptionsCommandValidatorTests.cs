using FluentValidation.TestHelper;
using Maran.Modules.Sites.Commands.ReplaceSiteOptions;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.Policies;

namespace Maran.Modules.Sites.Tests.Commands.ReplaceSiteOptions;

/// <summary>Field rules of <see cref="ReplaceSiteOptionsCommandValidator"/>.</summary>
public sealed class ReplaceSiteOptionsCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ReplaceSiteOptionsCommandValidator _validator = new();

    /// <summary>A command that states nothing passes.</summary>
    [Fact]
    public void A_command_that_states_nothing_passes()
    {
        _validator.TestValidate(Command()).ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>A command that fills in every group within its bounds passes.</summary>
    [Fact]
    public void A_command_that_fills_in_every_group_within_its_bounds_passes()
    {
        var command = Command() with
        {
            CanonicalHost = CanonicalHost.Www,
            ClientMaxBodySizeMib = 1024,
            StaticCacheDays = 365,
            Redirects = [new SiteRedirectDto("/blog/", RedirectMatch.Prefix, "https://example.org/blog/", RedirectStatus.Temporary)],
            BasicAuthUsers = [new SiteBasicAuthUserInputDto("alice", "long enough password"), new SiteBasicAuthUserInputDto("bob", null)],
            IpAccessMode = IpAccessMode.AllowList,
            IpRules = ["203.0.113.0/24", "2001:db8::/32"],
            RequestRate = new SiteRequestRateDto(1000, 1000),
        };

        _validator.TestValidate(command).ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>A command with no site is rejected.</summary>
    [Fact]
    public void A_command_with_no_site_is_rejected()
    {
        _validator.TestValidate(Command() with { SiteId = Guid.Empty })
            .ShouldHaveValidationErrorFor(command => command.SiteId);
    }

    /// <summary>A body size outside the bounds is rejected with its own key.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1025)]
    public void A_body_size_outside_the_bounds_is_rejected_with_its_own_key(int size)
    {
        var result = _validator.TestValidate(Command() with { ClientMaxBodySizeMib = size });

        result.ShouldHaveValidationErrorFor(command => command.ClientMaxBodySizeMib)
            .WithErrorMessage("SiteBodySizeOutOfRange");
    }

    /// <summary>A cache lifetime outside the bounds is rejected with its own key.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public void A_cache_lifetime_outside_the_bounds_is_rejected_with_its_own_key(int days)
    {
        var result = _validator.TestValidate(Command() with { StaticCacheDays = days });

        result.ShouldHaveValidationErrorFor(command => command.StaticCacheDays)
            .WithErrorMessage("SiteCacheDaysOutOfRange");
    }

    /// <summary>The bounds of the request limit themselves are accepted.</summary>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(1000, 1000)]
    [InlineData(10, 20)]
    public void The_bounds_of_the_request_limit_themselves_are_accepted(int perSecond, int burst)
    {
        _validator.TestValidate(Command() with { RequestRate = new SiteRequestRateDto(perSecond, burst) })
            .ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>A request rate outside the bounds is rejected with its own key.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void A_request_rate_outside_the_bounds_is_rejected_with_its_own_key(int perSecond)
    {
        var result = _validator.TestValidate(Command() with { RequestRate = new SiteRequestRateDto(perSecond, 5) });

        result.ShouldHaveValidationErrorFor(command => command.RequestRate)
            .WithErrorMessage("SiteRequestRateOutOfRange");
    }

    /// <summary>A request burst outside the bounds is rejected with its own key.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(1001)]
    public void A_request_burst_outside_the_bounds_is_rejected_with_its_own_key(int burst)
    {
        var result = _validator.TestValidate(Command() with { RequestRate = new SiteRequestRateDto(5, burst) });

        result.ShouldHaveValidationErrorFor(command => command.RequestRate)
            .WithErrorMessage("SiteRequestBurstOutOfRange");
    }

    /// <summary>A canonical host the enum does not have is rejected.</summary>
    [Fact]
    public void A_canonical_host_the_enum_does_not_have_is_rejected()
    {
        _validator.TestValidate(Command() with { CanonicalHost = (CanonicalHost)9 })
            .ShouldHaveValidationErrorFor(command => command.CanonicalHost)
            .WithErrorMessage("SiteOptionUnknown");
    }

    /// <summary>More redirect rules than the cap are rejected.</summary>
    [Fact]
    public void More_redirect_rules_than_the_cap_are_rejected()
    {
        var redirects = Enumerable.Range(0, SiteOptionsLimits.MaximumRedirects + 1)
            .Select(index =>
            {
                return new SiteRedirectDto($"/p{index}", RedirectMatch.Exact, "/x", RedirectStatus.Permanent);
            })
            .ToList();

        _validator.TestValidate(Command() with { Redirects = redirects })
            .ShouldHaveValidationErrorFor(command => command.Redirects)
            .WithErrorMessage("SiteRedirectLimit");
    }

    /// <summary>A redirect with a hostile path is rejected with the path key.</summary>
    [Theory]
    [InlineData("/a;b")]
    [InlineData("/a b")]
    [InlineData("/a\nb")]
    [InlineData("old")]
    [InlineData("/.well-known/acme-challenge/")]
    public void A_redirect_with_a_hostile_path_is_rejected_with_the_path_key(string path)
    {
        var result = _validator.TestValidate(
            Command() with { Redirects = [new SiteRedirectDto(path, RedirectMatch.Exact, "/new", RedirectStatus.Permanent)] });

        Assert.Contains(result.Errors, error =>
        {
            return error.ErrorMessage == "SiteRedirectPathInvalid";
        });
    }

    /// <summary>A redirect with a hostile target is rejected with the target key.</summary>
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("//evil.example")]
    [InlineData("https://example.com/a;b")]
    [InlineData("https://example.com/\"x")]
    [InlineData("")]
    public void A_redirect_with_a_hostile_target_is_rejected_with_the_target_key(string target)
    {
        var result = _validator.TestValidate(
            Command() with { Redirects = [new SiteRedirectDto("/old", RedirectMatch.Exact, target, RedirectStatus.Permanent)] });

        Assert.Contains(result.Errors, error =>
        {
            return error.ErrorMessage == "SiteRedirectTargetInvalid";
        });
    }

    /// <summary>A prefix rule must end in a slash, and an exact rule need not.</summary>
    [Fact]
    public void A_prefix_rule_must_end_in_a_slash_and_an_exact_rule_need_not()
    {
        var prefix = _validator.TestValidate(
            Command() with { Redirects = [new SiteRedirectDto("/blog", RedirectMatch.Prefix, "/news/", RedirectStatus.Permanent)] });
        var exact = _validator.TestValidate(
            Command() with { Redirects = [new SiteRedirectDto("/blog", RedirectMatch.Exact, "/news/", RedirectStatus.Permanent)] });

        Assert.Contains(prefix.Errors, error =>
        {
            return error.ErrorMessage == "SiteRedirectPathInvalid";
        });
        exact.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>An exact rule that redirects a path to itself is rejected.</summary>
    [Fact]
    public void An_exact_rule_that_redirects_a_path_to_itself_is_rejected()
    {
        var result = _validator.TestValidate(
            Command() with { Redirects = [new SiteRedirectDto("/loop", RedirectMatch.Exact, "/loop", RedirectStatus.Permanent)] });

        Assert.Contains(result.Errors, error =>
        {
            return error.ErrorMessage == "SiteRedirectTargetInvalid";
        });
    }

    /// <summary>Two rules for one path and comparison are rejected.</summary>
    [Fact]
    public void Two_rules_for_one_path_and_comparison_are_rejected()
    {
        var result = _validator.TestValidate(
            Command() with
            {
                Redirects =
                [
                    new SiteRedirectDto("/a", RedirectMatch.Exact, "/b", RedirectStatus.Permanent),
                    new SiteRedirectDto("/a", RedirectMatch.Exact, "/c", RedirectStatus.Temporary),
                ],
            });

        result.ShouldHaveValidationErrorFor(command => command.Redirects)
            .WithErrorMessage("SiteRedirectDuplicated");
    }

    /// <summary>The same path under an exact and a prefix comparison is two different rules.</summary>
    [Fact]
    public void The_same_path_under_an_exact_and_a_prefix_comparison_is_two_different_rules()
    {
        _validator.TestValidate(
            Command() with
            {
                Redirects =
                [
                    new SiteRedirectDto("/a/", RedirectMatch.Exact, "/b", RedirectStatus.Permanent),
                    new SiteRedirectDto("/a/", RedirectMatch.Prefix, "/c/", RedirectStatus.Permanent),
                ],
            })
            .ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>More users than the cap are rejected.</summary>
    [Fact]
    public void More_users_than_the_cap_are_rejected()
    {
        var users = Enumerable.Range(0, SiteOptionsLimits.MaximumBasicAuthUsers + 1)
            .Select(index =>
            {
                return new SiteBasicAuthUserInputDto($"user{index}", "long enough password");
            })
            .ToList();

        _validator.TestValidate(Command() with { BasicAuthUsers = users })
            .ShouldHaveValidationErrorFor(command => command.BasicAuthUsers)
            .WithErrorMessage("SiteBasicAuthLimit");
    }

    /// <summary>A user name that could break the credential file is rejected.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("a:b")]
    [InlineData("a b")]
    [InlineData("a\nb")]
    [InlineData("ünïcode")]
    [InlineData("0123456789012345678901234567890123")]
    public void A_user_name_that_could_break_the_credential_file_is_rejected(string name)
    {
        var result = _validator.TestValidate(
            Command() with { BasicAuthUsers = [new SiteBasicAuthUserInputDto(name, "long enough password")] });

        Assert.Contains(result.Errors, error =>
        {
            return error.ErrorMessage == "SiteBasicAuthUserInvalid";
        });
    }

    /// <summary>A stated password that is too short or too long is rejected, and an omitted one is not.</summary>
    [Fact]
    public void A_stated_password_that_is_too_short_or_too_long_is_rejected_and_an_omitted_one_is_not()
    {
        var tooShort = _validator.TestValidate(
            Command() with { BasicAuthUsers = [new SiteBasicAuthUserInputDto("alice", "short")] });
        var tooLong = _validator.TestValidate(
            Command() with { BasicAuthUsers = [new SiteBasicAuthUserInputDto("alice", new string('a', 73))] });
        var omitted = _validator.TestValidate(
            Command() with { BasicAuthUsers = [new SiteBasicAuthUserInputDto("alice", null)] });

        Assert.Contains(tooShort.Errors, error =>
        {
            return error.ErrorMessage == "SiteBasicAuthPasswordInvalid";
        });
        Assert.Contains(tooLong.Errors, error =>
        {
            return error.ErrorMessage == "SiteBasicAuthPasswordInvalid";
        });
        omitted.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>Two users with one name, whatever the case, are rejected.</summary>
    [Fact]
    public void Two_users_with_one_name_whatever_the_case_are_rejected()
    {
        _validator.TestValidate(
            Command() with
            {
                BasicAuthUsers =
                [
                    new SiteBasicAuthUserInputDto("Alice", null),
                    new SiteBasicAuthUserInputDto("alice", null),
                ],
            })
            .ShouldHaveValidationErrorFor(command => command.BasicAuthUsers)
            .WithErrorMessage("SiteBasicAuthUserDuplicated");
    }

    /// <summary>More addresses than the cap are rejected.</summary>
    [Fact]
    public void More_addresses_than_the_cap_are_rejected()
    {
        var rules = Enumerable.Range(0, SiteOptionsLimits.MaximumIpRules + 1)
            .Select(index =>
            {
                return $"10.0.{index}.0/24";
            })
            .ToList();

        _validator.TestValidate(Command() with { IpAccessMode = IpAccessMode.DenyList, IpRules = rules })
            .ShouldHaveValidationErrorFor(command => command.IpRules)
            .WithErrorMessage("SiteIpRuleLimit");
    }

    /// <summary>An address or range that is not valid is rejected.</summary>
    [Theory]
    [InlineData("not-an-address")]
    [InlineData("203.0.113.5/33")]
    [InlineData("203.0.113.5/24")]
    [InlineData("203.0.113.5; deny all")]
    [InlineData("203.0.113.5\nallow all")]
    public void An_address_or_range_that_is_not_valid_is_rejected(string rule)
    {
        var result = _validator.TestValidate(Command() with { IpAccessMode = IpAccessMode.AllowList, IpRules = [rule] });

        Assert.Contains(result.Errors, error =>
        {
            return error.ErrorMessage == "SiteIpRuleInvalid";
        });
    }

    /// <summary>The same address written two ways is rejected as a duplicate.</summary>
    [Fact]
    public void The_same_address_written_two_ways_is_rejected_as_a_duplicate()
    {
        _validator.TestValidate(
            Command() with { IpAccessMode = IpAccessMode.AllowList, IpRules = ["203.0.113.5", "203.0.113.5/32"] })
            .ShouldHaveValidationErrorFor(command => command.IpRules)
            .WithErrorMessage("SiteIpRuleDuplicated");
    }

    /// <summary>An access mode without addresses, and addresses without a mode, are both rejected.</summary>
    [Fact]
    public void An_access_mode_without_addresses_and_addresses_without_a_mode_are_both_rejected()
    {
        _validator.TestValidate(Command() with { IpAccessMode = IpAccessMode.AllowList })
            .ShouldHaveValidationErrorFor(command => command.IpAccessMode)
            .WithErrorMessage("SiteIpAccessModeInvalid");
        _validator.TestValidate(Command() with { IpRules = ["203.0.113.5"] })
            .ShouldHaveValidationErrorFor(command => command.IpAccessMode)
            .WithErrorMessage("SiteIpAccessModeInvalid");
    }

    /// <summary>Every rejection reports a resource key rather than an english sentence.</summary>
    [Fact]
    public void Every_rejection_reports_a_resource_key_rather_than_an_english_sentence()
    {
        var result = _validator.TestValidate(Command() with
        {
            ClientMaxBodySizeMib = 0,
            Redirects = [new SiteRedirectDto("bad", RedirectMatch.Exact, "bad", RedirectStatus.Permanent)],
            IpRules = ["x"],
        });

        Assert.NotEmpty(result.Errors);
        Assert.All(result.Errors, error =>
        {
            Assert.True(error.ErrorMessage.All(char.IsLetterOrDigit), error.ErrorMessage);
        });
    }

    /// <summary>Builds a valid command that states nothing; a test varies the one group it is about.</summary>
    private static ReplaceSiteOptionsCommand Command()
    {
        return new ReplaceSiteOptionsCommand(
            Guid.NewGuid(),
            CanonicalHost.None,
            null,
            null,
            [],
            [],
            null,
            [],
            null,
            "198.51.100.7",
            "tests");
    }
}
