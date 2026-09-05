using System.Text.RegularExpressions;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Errors;

/// <summary>
/// The single place a wire <see cref="AgentError"/> becomes something the panel may carry: a typed
/// <see cref="Error"/> holding nothing but a machine-stable code, plus one log line holding the
/// agent's own text.
/// </summary>
/// <remarks>
/// This was five identical private copies, one per client. They agreed at the time, which is the
/// argument for having one: the moment a copy is edited alone the panel has two opinions about what
/// may reach a customer, and every future redaction has to be applied five times and stay applied.
/// The private-key redaction below is exactly that case — it was needed on the TLS client, and it is
/// wanted on all of them, because any agent message may quote material it failed to parse.
///
/// Two invariants live here and nowhere else:
/// the returned <see cref="Error"/> carries a code and never the agent's sentence, which is
/// operator-facing and can name absolute paths on the host (rules/security.md item 8); and the text
/// that does get logged has its key material removed first — armoured PEM blocks and the bare
/// base64 fragments a parser quotes alike — so a private key the agent echoed back cannot land in
/// the panel's log.
/// </remarks>
internal static partial class AgentErrorTranslator
{
    /// <summary>What replaces a PEM block in text on its way to the log.</summary>
    private const string RedactedPem = "[pem redacted]";

    /// <summary>What replaces a bare run of key-shaped characters in text on its way to the log.</summary>
    private const string RedactedSecret = "[redacted]";

    /// <summary>
    /// Pre-compiled log delegate for a failure the agent reported. Source-generated because an agent
    /// that is refusing fails every call at once.
    /// </summary>
    private static readonly Action<ILogger, string, string, string, Exception?> LogAgentError =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Warning,
            new EventId(1, nameof(AgentErrorTranslator)),
            "Agent refused {Operation} with {AgentErrorCode}: {AgentErrorMessage}");

    /// <summary>
    /// Converts a wire failure into a typed error, logging the agent's own sentence and any tool
    /// output — with key material stripped — on the way.
    /// </summary>
    /// <param name="logger">Where the agent's operator-facing text goes.</param>
    /// <param name="error">The failure payload returned by the agent.</param>
    /// <param name="operation">Which call refused, so the log line names it.</param>
    /// <returns>The error carrying only a machine-stable code.</returns>
    public static Error ToError(ILogger logger, AgentError error, string operation)
    {
        return ToError(logger, error, operation, null);
    }

    /// <summary>
    /// Converts a wire failure into a typed error, logging the agent's own sentence and any tool
    /// output — with key material and the secret this call itself sent stripped — on the way.
    /// </summary>
    /// <param name="logger">Where the agent's operator-facing text goes.</param>
    /// <param name="error">The failure payload returned by the agent.</param>
    /// <param name="operation">Which call refused, so the log line names it.</param>
    /// <param name="sentSecret">
    /// The secret this call carried to the agent, when it carried one. Supplied so that the agent
    /// quoting it back cannot put it in the panel's log.
    /// </param>
    /// <returns>The error carrying only a machine-stable code.</returns>
    /// <remarks>
    /// The pattern rules below catch key material because key material has a shape. A database or
    /// SFTP password has no shape at all — it is a short run of mixed characters, indistinguishable
    /// from a hostname or a table name — so no regex can find it, and the agent's most natural way
    /// of reporting a refused credential is to quote it (<c>Access denied for 'acc1_shop'@'localhost'
    /// (using password: 'Xk7…')</c>). What makes it findable is the one thing the panel does know:
    /// it minted the value seconds ago, so it can look for that exact string rather than for a
    /// pattern. Recognising what we sent is strictly stronger than guessing what a secret looks
    /// like, and it is the only rule that would have caught this one.
    /// </remarks>
    public static Error ToError(ILogger logger, AgentError error, string operation, SensitiveString? sentSecret)
    {
        var code = ToErrorCode(error.Code);

        // Logged and never returned, so no path and no tool excerpt can render to a customer.
        LogAgentError(
            logger,
            operation,
            code,
            Redact($"{error.Message} {error.ToolOutput}".Trim(), sentSecret),
            null);

        return Error.Of(code, ToErrorType(error.Code));
    }

    /// <summary>Maps a wire <see cref="ErrorCode"/> to the kind of failure it is.</summary>
    /// <param name="code">The failure category reported by the agent.</param>
    /// <returns>The <see cref="ErrorType"/> the panel answers that category with.</returns>
    /// <remarks>
    /// Kept beside <see cref="ToErrorCode"/> and switching on the same value, so the code and the
    /// kind cannot drift: an arm added to one is an arm missing from the other, and the compiler's
    /// exhaustiveness warning names the second. The unspecified arm is <see cref="ErrorType.Failure"/>
    /// rather than a 400 — an agent that will not say what went wrong has failed, and blaming the
    /// caller for it is both wrong and untestable.
    /// </remarks>
    public static ErrorType ToErrorType(ErrorCode code)
    {
        return code switch
        {
            ErrorCode.Unspecified => ErrorType.Failure,
            ErrorCode.InvalidInput => ErrorType.Validation,
            ErrorCode.AlreadyExists => ErrorType.Conflict,
            ErrorCode.NotFound => ErrorType.NotFound,
            ErrorCode.ValidationFailed => ErrorType.Validation,
            ErrorCode.SystemFailure => ErrorType.Failure,
            _ => ErrorType.Failure,
        };
    }

    /// <summary>Maps a wire <see cref="ErrorCode"/> to its stable "Agent*" error code string.</summary>
    /// <param name="code">The failure category reported by the agent.</param>
    /// <returns>The machine-stable code the resources translate.</returns>
    /// <remarks>
    /// The two stream codes have no arm of their own. They are not failures of an operation but ways
    /// a stream ended, and the streaming clients turn them into typed terminal events before they
    /// ever reach here; a stream code arriving on a unary call is the agent misbehaving, so it takes
    /// the unspecified arm.
    /// </remarks>
    public static string ToErrorCode(ErrorCode code)
    {
        return code switch
        {
            ErrorCode.Unspecified => nameof(ErrorMessages.AgentUnspecified),
            ErrorCode.InvalidInput => nameof(ErrorMessages.AgentInvalidInput),
            ErrorCode.AlreadyExists => nameof(ErrorMessages.AgentAlreadyExists),
            ErrorCode.NotFound => nameof(ErrorMessages.AgentNotFound),
            ErrorCode.ValidationFailed => nameof(ErrorMessages.AgentValidationFailed),
            ErrorCode.SystemFailure => nameof(ErrorMessages.AgentSystemFailure),
            _ => nameof(ErrorMessages.AgentUnspecified),
        };
    }

    /// <summary>Removes key material and any secret this call sent from text that is about to be logged.</summary>
    /// <param name="text">The agent's message and its tool output, joined by a space.</param>
    /// <param name="sentSecret">The secret this call carried to the agent, or null when it carried none.</param>
    /// <returns>
    /// The same text with the sent secret, every PEM block and every bare run of key-shaped bytes
    /// replaced.
    /// </returns>
    /// <remarks>
    /// A certificate install hands the agent a private key, and the natural way to report that it
    /// could not be parsed is to quote what could not be parsed. That quotation has two shapes and
    /// both are redacted here, in this order:
    ///
    /// The armoured block — everything from a BEGIN marker to its END marker, and an unterminated
    /// block takes the rest of the string with it. First, so a whole key disappears as one unit
    /// rather than as scattered pieces.
    ///
    /// The bare fragment — <c>invalid base64 at line 3: MIIEvQIBADANBgkqhkiG9w0…</c>, which carries
    /// the key body with no marker for the first rule to anchor on. Any unbroken run of forty or
    /// more base64 characters goes: an operator never needs to read one, and no English diagnostic
    /// and no filesystem path reaches that length without a space, a dot, a dash or a colon
    /// breaking it. The threshold is deliberately below the ~64 characters of one wrapped PEM line,
    /// so a single quoted line is caught too.
    ///
    /// A truncated diagnostic is a smaller loss than a key in the log; the operator still has the
    /// error code and the operation name. The two inputs are joined by a space before this runs, so
    /// a run spanning the join redacts as two runs rather than escaping as one — two redactions is
    /// an acceptable outcome, a missed one is not.
    ///
    /// The sent secret goes FIRST, before either pattern, for two reasons: it is knowledge rather
    /// than a guess, and running it first means a password that happens to be forty base64-shaped
    /// characters is removed as a secret we recognise rather than left to a pattern that might not
    /// span it. It is matched literally and case-sensitively, because it is compared against the
    /// same bytes that went out.
    /// </remarks>
    private static string Redact(string text, SensitiveString? sentSecret)
    {
        var withoutSentSecret = RemoveSentSecret(text, sentSecret);
        var withoutPemBlocks = PemBlock().Replace(withoutSentSecret, RedactedPem);

        return Base64Run().Replace(withoutPemBlocks, RedactedSecret);
    }

    /// <summary>Replaces every occurrence of the secret this call sent, when there was one.</summary>
    /// <param name="text">The agent's text.</param>
    /// <param name="sentSecret">The secret this call carried to the agent, or null.</param>
    /// <returns>The text with the secret masked, or the text unchanged when there is nothing to look for.</returns>
    /// <remarks>
    /// The length floor is <see cref="SecretRedactionPolicy"/>'s rather than this file's, because it
    /// binds the code that MINTS secrets as much as this: a shorter value silently gets no literal
    /// redaction at all, and the caller — not this file — is where a generator that produced one
    /// would have to be fixed. Stating it in a shared, public place is what lets a generator's own
    /// test hold itself to it.
    /// </remarks>
    private static string RemoveSentSecret(string text, SensitiveString? sentSecret)
    {
        if (sentSecret is null)
        {
            return text;
        }

        var value = sentSecret.Reveal();

        if (value.Length < SecretRedactionPolicy.ShortestRecognisableSecret)
        {
            return text;
        }

        return text.Replace(value, RedactedSecret, StringComparison.Ordinal);
    }

    /// <summary>The PEM block pattern: a BEGIN marker through its END marker, or through the end.</summary>
    /// <returns>The generated matcher.</returns>
    [GeneratedRegex(@"-----BEGIN[\s\S]*?(?:-----END[^-]*-----|$)")]
    private static partial Regex PemBlock();

    /// <summary>The bare-material pattern: an unbroken run of forty or more base64 characters.</summary>
    /// <returns>The generated matcher.</returns>
    [GeneratedRegex("[A-Za-z0-9+/=]{40,}")]
    private static partial Regex Base64Run();
}
