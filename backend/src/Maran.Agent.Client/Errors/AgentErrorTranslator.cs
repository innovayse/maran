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
internal static class AgentErrorTranslator
{
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
            ErrorCode.NotImplemented => ErrorType.Unavailable,
            ErrorCode.LimitReached => ErrorType.Conflict,
            ErrorCode.PackageSourceRefused => ErrorType.UpstreamRefused,
            ErrorCode.PackageSourceUnreachable => ErrorType.Unavailable,
            ErrorCode.PackageNotAvailable => ErrorType.Conflict,
            ErrorCode.BelowFreeSpaceFloor => ErrorType.Unavailable,
            ErrorCode.AccountSuspended => ErrorType.Conflict,
            ErrorCode.AgentSaturated => ErrorType.Unavailable,
            ErrorCode.RunNotStopped => ErrorType.Failure,
            ErrorCode.QuotaExceeded => ErrorType.Conflict,
            ErrorCode.ArchiveUnsafe => ErrorType.Validation,
            ErrorCode.TooLarge => ErrorType.Validation,
            ErrorCode.ArchiveUnopenable => ErrorType.Validation,
            ErrorCode.AppNotInstalled => ErrorType.NotFound,
            ErrorCode.AppInUse => ErrorType.Conflict,
            ErrorCode.WebServerAlreadyInstalled => ErrorType.Conflict,
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
    ///
    /// <see cref="ErrorCode.NotImplemented"/> does have an arm, and it is the reason this list is
    /// worth auditing whenever the contract grows one. It is how the agent refuses an operation this
    /// build cannot perform at all — today, every backup destination naming an object store — and
    /// without the arm it fell to the unspecified default, telling an operator "something went wrong
    /// on your server" about a feature that was never built. Its KIND is
    /// <see cref="ErrorType.Unavailable"/> (503) rather than <see cref="ErrorType.Validation"/> or
    /// <see cref="ErrorType.Failure"/>: the caller asked for something reasonable, so blaming the
    /// request would be wrong, and nothing on the server broke, so a 500 would send the operator
    /// looking for a fault that does not exist. The operation is simply not available from this
    /// agent build, which is what a 503 states.
    ///
    /// <see cref="ErrorCode.LimitReached"/> is the agent refusing an operation whose request stated
    /// a plan allowance the account has already reached. Its kind is
    /// <see cref="ErrorType.Conflict"/>, the same 409 the panel answers its own full-plan refusals
    /// with: the request is well formed and the state of the account is what says no. Without an arm
    /// it would take the unspecified default and tell a customer "something went wrong on your
    /// server" about a plan limit they can act on. A module that wants to say more than the generic
    /// sentence here — that this was the CONCURRENT loser rather than the ordinary refusal — maps
    /// this code again in its own translator, which is what
    /// <c>Maran.Modules.Cron.Mappers.CronAgentErrorTranslator</c> does.
    ///
    /// <see cref="ErrorCode.PackageSourceRefused"/> and <see cref="ErrorCode.PackageSourceUnreachable"/>
    /// are a package install the agent classified as failing outside this host (issue #251). A source
    /// that answered with an HTTP error is <see cref="ErrorType.UpstreamRefused"/> (502): the panel's
    /// own server did its part and a party it depends on said no. A source nobody could reach is
    /// <see cref="ErrorType.Unavailable"/> (503): nothing answered, and a later attempt may succeed
    /// unchanged. Neither is a 500, which would send the operator looking for a fault on a machine
    /// that has none.
    ///
    /// <see cref="ErrorCode.BelowFreeSpaceFloor"/> is the agent refusing to start a write because the
    /// filesystem it would write into is within its free-space floor of full (issue #148). Its kind is
    /// <see cref="ErrorType.Unavailable"/> (503): the request is sound and nothing broke — the server
    /// cannot take on more data until space is freed, and nothing was changed. It is not a 409,
    /// because the caller's own state is not what refuses, and not a 500, because no fault occurred.
    /// It travels as its own code, <c>AgentBelowFreeSpaceFloor</c>, so a failed backup task reads
    /// "the server is nearly out of disk space" rather than a generic failure.
    ///
    /// <see cref="ErrorCode.AccountSuspended"/> is the agent refusing to act for an account the panel
    /// has suspended (issue #158: a cron entry run by hand). Its kind is <see cref="ErrorType.Conflict"/>
    /// (409): the request is well formed and the account's own state is what refuses it, exactly as for
    /// <see cref="ErrorCode.LimitReached"/>, and nothing was started.
    ///
    /// <see cref="ErrorCode.AgentSaturated"/> is the agent refusing a cron run by hand because it
    /// already runs as many as it admits, for the host or for the account (issues #333 and #331).
    /// Its kind is <see cref="ErrorType.Unavailable"/> (503) and not <see cref="ErrorType.Conflict"/>:
    /// nothing about the request or the account's state is wrong, and the answer changes by itself
    /// within the run's runtime ceiling, which is exactly what a 503 tells a caller to wait for.
    /// It is not <see cref="ErrorCode.LimitReached"/> (the panel's plan allowance, which waiting does
    /// not clear) and not a failure (nothing broke), and nothing was started.
    ///
    /// <see cref="ErrorCode.RunNotStopped"/> is the agent reporting that a cron run by hand was ended
    /// by a ceiling and that the transient unit it ran in could not be confirmed stopped (issue #336),
    /// so the job MAY STILL BE RUNNING. Its kind is <see cref="ErrorType.Failure"/> (500): something on
    /// the server did break and the outcome is genuinely unknown, so it is neither a 503 that invites
    /// the same call again (a retry would start a second run beside the first) nor a 409 that blames
    /// the account's state. It has its own code and does not ride on
    /// <see cref="ErrorCode.SystemFailure"/>, whose generic sentence says the request failed and
    /// leaves the host as it was; a module that knows what the code means says so in its own words, as
    /// <c>Maran.Modules.Cron.Mappers.CronAgentErrorTranslator</c> does.
    ///
    /// <see cref="ErrorCode.QuotaExceeded"/> is the account's disk quota ending a write (issue #167).
    /// Its kind is <see cref="ErrorType.Conflict"/> (409): the request is well-formed and disagrees
    /// with state the customer owns, a quota already spent, and repeating it unchanged meets the same
    /// quota. It is not <see cref="ErrorCode.BelowFreeSpaceFloor"/>, which is the host running short
    /// for everyone and answers 503. <see cref="ErrorCode.ArchiveUnsafe"/> is an archive refused as a
    /// whole because it holds a member the agent will not write; its kind is
    /// <see cref="ErrorType.Validation"/> (400), because the customer can send a different archive,
    /// and its sentence names no member: a member name is the customer's data and belongs in the
    /// journal of the module that called, never in the answer.
    ///
    /// <see cref="ErrorCode.TooLarge"/> is the agent refusing to open a file larger than the size the
    /// request allowed, or a directory holding more names than a listing can describe (issue #166).
    /// Its kind is <see cref="ErrorType.Validation"/> (400): the request as asked cannot be answered
    /// and asking again meets the same size, so it is neither a 503 that invites a retry nor a 500,
    /// because nothing broke and nothing was changed. It travels as its own code so a module can tell
    /// the customer the file or folder is too large to open here, which no generic sentence says.
    ///
    /// <see cref="ErrorCode.ArchiveUnopenable"/> is a sealed backup archive that did not open under
    /// the key the call carried, or that was sent none (issue #445). Its kind is
    /// <see cref="ErrorType.Validation"/> (400): the pair of archive and key is what the agent
    /// refused, nothing was changed, and repeating the call unchanged meets the same key. The code is
    /// named for what the agent knows and no more: the cipher authenticates every chunk, so a wrong
    /// key and a damaged sealed file fail identically, and the agent cannot say "wrong key". It
    /// travels as its own code so the backups module can tell the operator to check the destination's
    /// archive key, which the shared validation sentence ("correct these details") does not say.
    ///
    /// <see cref="ErrorCode.AppNotInstalled"/>, <see cref="ErrorCode.AppInUse"/> and
    /// <see cref="ErrorCode.WebServerAlreadyInstalled"/> are the three refusals of the app store
    /// (issue #517). Each is a state the host is in and the request cannot change: removing or
    /// controlling an app that is not installed is <see cref="ErrorType.NotFound"/> (the app is not
    /// there), and removing an app a site still uses, or installing a second site web server, is
    /// <see cref="ErrorType.Conflict"/> (the host's state forbids the request as asked). They travel
    /// as their own codes so the page can say which app and why, which the generic validation
    /// sentence does not.
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
            ErrorCode.NotImplemented => nameof(ErrorMessages.AgentNotImplemented),
            ErrorCode.LimitReached => nameof(ErrorMessages.AgentLimitReached),
            ErrorCode.PackageSourceRefused => nameof(ErrorMessages.AgentPackageSourceRefused),
            ErrorCode.PackageSourceUnreachable => nameof(ErrorMessages.AgentPackageSourceUnreachable),
            ErrorCode.PackageNotAvailable => nameof(ErrorMessages.AgentPackageNotAvailable),
            ErrorCode.BelowFreeSpaceFloor => nameof(ErrorMessages.AgentBelowFreeSpaceFloor),
            ErrorCode.AccountSuspended => nameof(ErrorMessages.AgentAccountSuspended),
            ErrorCode.AgentSaturated => nameof(ErrorMessages.AgentSaturated),
            ErrorCode.RunNotStopped => nameof(ErrorMessages.AgentRunNotStopped),
            ErrorCode.QuotaExceeded => nameof(ErrorMessages.AgentQuotaExceeded),
            ErrorCode.ArchiveUnsafe => nameof(ErrorMessages.AgentArchiveUnsafe),
            ErrorCode.TooLarge => nameof(ErrorMessages.AgentTooLarge),
            ErrorCode.ArchiveUnopenable => nameof(ErrorMessages.AgentArchiveUnopenable),
            ErrorCode.AppNotInstalled => nameof(ErrorMessages.AgentAppNotInstalled),
            ErrorCode.AppInUse => nameof(ErrorMessages.AgentAppInUse),
            ErrorCode.WebServerAlreadyInstalled => nameof(ErrorMessages.AgentWebServerAlreadyInstalled),
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
        return SecretTextRedactor.RedactKeyMaterial(text, sentSecret);
    }
}
