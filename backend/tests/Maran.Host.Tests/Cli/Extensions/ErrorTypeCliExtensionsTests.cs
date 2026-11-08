using Maran.Host.Cli.Enums;
using Maran.Host.Cli.Extensions;
using Maran.SharedKernel.Results;

namespace Maran.Host.Tests.Cli.Extensions;

/// <summary>How a failure's kind becomes a verb's exit status.</summary>
public sealed class ErrorTypeCliExtensionsTests
{
    /// <summary>An unreachable dependency is the unavailable status.</summary>
    [Fact]
    public void An_unreachable_dependency_is_the_unavailable_status()
    {
        Assert.Equal(CliExitCode.Unavailable, ErrorType.Unavailable.ToCliExitCode());
    }

    /// <summary>A fault and an outside party's refusal are a plain failure.</summary>
    [Theory]
    [InlineData(ErrorType.Failure)]
    [InlineData(ErrorType.UpstreamRefused)]
    public void A_fault_and_an_outside_partys_refusal_are_a_plain_failure(ErrorType type)
    {
        Assert.Equal(CliExitCode.Failure, type.ToCliExitCode());
    }

    /// <summary>Every other kind is a refusal by policy.</summary>
    [Theory]
    [InlineData(ErrorType.Conflict)]
    [InlineData(ErrorType.NotFound)]
    [InlineData(ErrorType.Validation)]
    public void Every_other_kind_is_a_refusal_by_policy(ErrorType type)
    {
        Assert.Equal(CliExitCode.Refused, type.ToCliExitCode());
    }
}
