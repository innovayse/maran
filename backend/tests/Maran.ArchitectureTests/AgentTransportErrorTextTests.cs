using Grpc.Core;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Services.SystemService;
using Maran.SharedKernel.Results;

namespace Maran.ArchitectureTests;

/// <summary>
/// Every typed failure the panel's agent client can answer with — a transport failure or a refused
/// contract revision — has text a customer can read in all three shipped languages.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ResourceKeyParityTests"/> proves the three files carry the same keys. It cannot see
/// that a key an error CODE names exists at all, nor that a translation is not the English text pasted
/// into the Russian file, which is the other way a Russian or Armenian customer ends up reading
/// English. This law starts from the codes the client really produces — asked of the production
/// classifier, not listed by hand — so a new variant added to <see cref="AgentTransportFailure"/>
/// without text is a build failure here and not a blank error message on a customer's screen.
/// </para>
/// <para>
/// UNOBSERVED HERE: which of these codes a route can currently return, and whether a translation is
/// correct rather than merely present and different. The text is read from the authored files, not
/// from the compiled satellite assemblies.
/// </para>
/// </remarks>
public sealed class AgentTransportErrorTextTests
{
    /// <summary>The path, relative to <c>backend/src</c>, of the agent client's resource family.</summary>
    private const string AgentClientResources = "Maran.Agent.Client/Resources/ErrorMessages.resx";

    /// <summary>The four codes the panel documents, in ordinal order.</summary>
    private static readonly string[] DocumentedCodes =
        ["AgentIncompatible", "AgentInvalidResponse", "AgentNotImplemented", "AgentUnavailable"];

    /// <summary>Every typed error code the agent client's transport and handshake can produce.</summary>
    /// <returns>The distinct codes, asked of the production code.</returns>
    private static List<string> ProducedCodes()
    {
        var statuses = Enum.GetValues<StatusCode>();
        var fromStatuses = statuses.Select(status =>
        {
            return AgentTransportFailure.ToError(new RpcException(new Status(status, string.Empty))).Code;
        });

        return fromStatuses
            .Append(AgentTransportFailure.Unavailable().Code)
            .Append(AgentProtoVersionWindow.IncompatibleCode)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The classifier produces the four variants the panel documents, no more and no fewer.</summary>
    [Fact]
    public void The_transport_classifier_produces_exactly_the_documented_error_variants()
    {
        Assert.Equal(DocumentedCodes, ProducedCodes());
    }

    /// <summary>Every code the client produces is written in the neutral file and both translations.</summary>
    /// <param name="culture">The culture under test, or <c>null</c> for the neutral English file.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("ru")]
    [InlineData("hy")]
    public void Every_agent_client_error_code_has_text_in_every_shipped_language(string? culture)
    {
        var neutral = ResourceTriples.NeutralPathOf(AgentClientResources);
        var path = culture is null ? neutral : ResourceTriples.TranslatedPathOf(neutral, culture);
        var entries = ResourceTriples.Read(path);

        var blank = ProducedCodes()
            .Where(code =>
            {
                return !entries.TryGetValue(code, out var text) || string.IsNullOrWhiteSpace(text);
            })
            .ToList();

        Assert.True(
            blank.Count == 0,
            $"{Path.GetFileName(path)} has no text for: {string.Join(", ", blank)}.");
    }

    /// <summary>A translation is a translation: neither the English sentence nor another language's copy of it.</summary>
    [Fact]
    public void Every_agent_client_error_code_is_translated_not_copied()
    {
        var neutral = ResourceTriples.NeutralPathOf(AgentClientResources);
        var english = ResourceTriples.Read(neutral);
        var russian = ResourceTriples.Read(ResourceTriples.TranslatedPathOf(neutral, "ru"));
        var armenian = ResourceTriples.Read(ResourceTriples.TranslatedPathOf(neutral, "hy"));

        var copies = new List<string>();
        foreach (var code in ProducedCodes())
        {
            var texts = new[] { english[code], russian[code], armenian[code] };
            if (texts.Distinct(StringComparer.Ordinal).Count() != texts.Length)
            {
                copies.Add(code);
            }
        }

        Assert.True(
            copies.Count == 0,
            $"These codes read the same in two languages: {string.Join(", ", copies)}.");
    }

    /// <summary>
    /// A customer-facing sentence never carries the transport's own vocabulary, which names the socket
    /// and the host's internals (rules/security.md item 8).
    /// </summary>
    /// <param name="culture">The culture under test, or <c>null</c> for the neutral English file.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("ru")]
    [InlineData("hy")]
    public void An_unavailable_or_incompatible_answer_does_not_leak_transport_detail(string? culture)
    {
        var neutral = ResourceTriples.NeutralPathOf(AgentClientResources);
        var path = culture is null ? neutral : ResourceTriples.TranslatedPathOf(neutral, culture);
        var entries = ResourceTriples.Read(path);

        foreach (var code in new[] { "AgentUnavailable", "AgentIncompatible" })
        {
            var text = entries[code];
            foreach (var forbidden in new[] { ".sock", "/run/", "gRPC", "Grpc", "SocketException", "HttpRequest", "proto" })
            {
                Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>Both outage codes are the kind that answers 503, so a client is told to retry.</summary>
    [Fact]
    public void The_outage_and_incompatibility_failures_are_of_the_unavailable_kind()
    {
        Assert.Equal(ErrorType.Unavailable, AgentTransportFailure.Unavailable().Type);
        Assert.Equal(
            ErrorType.Unavailable,
            AgentTransportFailure.ToError(new RpcException(new Status(StatusCode.Unavailable, string.Empty))).Type);
        Assert.Equal(
            ErrorType.Unavailable,
            AgentTransportFailure.ToError(new RpcException(new Status(StatusCode.DeadlineExceeded, string.Empty))).Type);
    }
}
