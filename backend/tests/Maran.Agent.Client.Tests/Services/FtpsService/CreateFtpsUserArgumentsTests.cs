using Maran.Agent.Client.Services.FtpsService;
using Maran.SharedKernel.Security;

namespace Maran.Agent.Client.Tests.Services.FtpsService;

/// <summary>What the creation carrier prints, which is everything about it except the password.</summary>
/// <remarks>
/// A record's compiler-generated <c>ToString()</c> is the leak this type is shaped against: it
/// prints every property, so a bare <c>string</c> password would reach the first log line that
/// interpolated the request. The assertions below are paired on purpose — "does not contain the
/// password" alone is satisfied by a printer that emits nothing at all, so the same string is also
/// asserted to carry the two members that are NOT secret.
/// </remarks>
public sealed class CreateFtpsUserArgumentsTests
{
    /// <summary>A real generated password, long enough that its absence is a fact about the printer.</summary>
    private const string GeneratedPassword = "Qm4-brisk-otter-91";

    /// <summary>The generated string prints the account and the login and never the password.</summary>
    [Fact]
    public void The_generated_string_prints_the_account_and_the_login_and_never_the_password()
    {
        var carrier = new CreateFtpsUserArguments("alice", "files", new SensitiveString(GeneratedPassword));

        var printed = carrier.ToString();

        // Positive control: the record does print its members, and these two really are in it — so
        // the absence below is the wrapper working rather than the printer being empty.
        Assert.Contains("alice", printed, StringComparison.Ordinal);
        Assert.Contains("files", printed, StringComparison.Ordinal);

        Assert.DoesNotContain(GeneratedPassword, printed, StringComparison.Ordinal);
        Assert.Contains("Password = [redacted]", printed, StringComparison.Ordinal);
    }

    /// <summary>An empty password would prove nothing, so the control is asserted on a real value.</summary>
    /// <remarks>
    /// The reason this test exists beside the one above: a leak check whose needle is the empty
    /// string passes against any output whatever. Here the mask is asserted to be present for a
    /// value that is emphatically not empty, and the revealed value is asserted to survive the trip
    /// through the carrier — a wrapper that had quietly discarded the secret would satisfy every
    /// "does not contain" assertion in this file and break creation on a customer's server.
    /// </remarks>
    [Fact]
    public void The_carrier_still_hands_the_real_password_to_a_caller_that_asks_for_it()
    {
        var carrier = new CreateFtpsUserArguments("alice", "files", new SensitiveString(GeneratedPassword));

        Assert.Equal(GeneratedPassword, carrier.Password.Reveal());
        Assert.NotEqual(carrier.Password.Reveal(), carrier.Password.ToString());
    }
}
