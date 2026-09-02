using Maran.SharedKernel.Security;

namespace Maran.SharedKernel.Tests.Security;

/// <summary>What a SensitiveString does: hands the value over on request, and prints nothing.</summary>
/// <remarks>
/// The leak this guards against is not exotic. A C# <c>record</c> generates a <c>ToString()</c> over
/// every property, so a request type with a <c>Password</c> property spills it the first time anyone
/// logs the request or interpolates it into a message — the same shape that put a private key in a
/// log in the previous plan. These tests are the reason the carrier is a sealed class with a written
/// <c>ToString</c> rather than a record, and they fail loudly if it is ever turned back into one.
/// </remarks>
public sealed class SensitiveStringTests
{
    /// <summary>The secret this suite wraps; distinctive so a substring assertion cannot pass by luck.</summary>
    private const string Secret = "Tz7-quiet-mule-42";

    /// <summary>Reveal hands back exactly what was wrapped.</summary>
    [Fact]
    public void Reveal_hands_back_exactly_what_was_wrapped()
    {
        Assert.Equal(Secret, new SensitiveString(Secret).Reveal());
    }

    /// <summary>ToString yields the mask and never the value.</summary>
    [Fact]
    public void ToString_yields_the_mask_and_never_the_value()
    {
        var rendered = new SensitiveString(Secret).ToString();

        Assert.DoesNotContain(Secret, rendered, StringComparison.Ordinal);
        Assert.Equal("[redacted]", rendered);
    }

    /// <summary>String interpolation which calls ToString yields the mask and never the value.</summary>
    /// <remarks>
    /// Separate from the direct call deliberately: interpolation is how the leak actually happens —
    /// nobody writes <c>password.ToString()</c>, they write <c>$"... {password}"</c> — and a compiler
    /// that ever chose a different conversion here would slip past a test of the method alone.
    /// </remarks>
    [Fact]
    public void String_interpolation_which_calls_ToString_yields_the_mask_and_never_the_value()
    {
        var password = new SensitiveString(Secret);

        var rendered = $"setting password {password} for alice";

        Assert.DoesNotContain(Secret, rendered, StringComparison.Ordinal);
        Assert.Equal("setting password [redacted] for alice", rendered);
    }

    /// <summary>Formatting the wrapper as a boxed object yields the mask and never the value.</summary>
    /// <remarks>
    /// The route a structured logger takes: the value arrives as an <see cref="object"/> argument and
    /// is rendered by <see cref="string.Format(string, object?)"/>, which calls the virtual
    /// <c>ToString</c>. Covered on its own because it is the path no call site can see.
    /// </remarks>
    [Fact]
    public void Formatting_the_wrapper_as_a_boxed_object_yields_the_mask_and_never_the_value()
    {
        object boxed = new SensitiveString(Secret);

        var rendered = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", boxed);

        Assert.DoesNotContain(Secret, rendered, StringComparison.Ordinal);
        Assert.Equal("[redacted]", rendered);
    }

    /// <summary>An empty secret is wrapped rather than refused so validation stays the callers job.</summary>
    [Fact]
    public void An_empty_secret_is_wrapped_rather_than_refused_so_validation_stays_the_callers_job()
    {
        Assert.Equal(string.Empty, new SensitiveString(string.Empty).Reveal());
    }
}
