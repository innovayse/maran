namespace Maran.SharedKernel.Results;

/// <summary>
/// What KIND of failure an <see cref="Error"/> is, and the only thing an HTTP status is ever derived
/// from (<c>ApiResultExtensions.MapStatusCode</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The kind lives on the error, not in the mapper.</b> The status used to be inferred from the
/// code's spelling — a chain of <c>code.EndsWith("NotFound")</c> arms with a 400 default — and that
/// design failed twice in one repository. Once loudly: the suffixes were matched in a dotted
/// lower-case form, the codes became PascalCase, and every missing account and every duplicate
/// domain silently answered 400. Once quietly, and it was still true when this enum was added:
/// eighteen codes that name a SERVER failure — <c>AcmeAuthorityUnreachable</c>,
/// <c>DatabaseProvisioningFailed</c>, <c>MailDeliveryFailed</c>, <c>AgentSystemFailure</c> — fell
/// through to the default and told the customer their request was malformed.
/// </para>
/// <para>
/// Both failures have the same shape: the spelling of a name was load-bearing, and nothing checked
/// it. So <see cref="Error"/> takes the kind as a constructor argument with no inferring overload —
/// a new error code cannot be written without answering this question, and the compiler is what
/// asks. The mapper then has exactly one arm per value here and no knowledge of any code at all,
/// which is what makes "the same class of failure answers the same status in every module" a
/// property of the type system rather than of everyone's care.
/// </para>
/// </remarks>
public enum ErrorType
{
    /// <summary>The caller sent something they could have sent differently. Answers 400.</summary>
    Validation,

    /// <summary>
    /// The named thing does not exist, or does not exist FOR THIS CALLER. Answers 404.
    /// </summary>
    /// <remarks>
    /// The second half is the tenancy rule and it is not optional: a resource that belongs to
    /// another account is <see cref="NotFound"/>, never <see cref="Forbidden"/>. A 403 confirms the
    /// row exists, which is the whole of the information an enumeration attack wants, and it
    /// contradicts the IDOR test every tenant entity carries (rules/security.md item 6). Tenant
    /// scoping produces this answer on its own — a global query filter hides the row and the handler
    /// finds nothing — so reaching for <see cref="Forbidden"/> on a tenant resource means the filter
    /// was bypassed and the code is answering a question it should not have been able to ask.
    /// </remarks>
    NotFound,

    /// <summary>
    /// The request is well-formed but disagrees with state that already exists — a taken name, a
    /// duplicate entry, a quota already spent. Answers 409.
    /// </summary>
    Conflict,

    /// <summary>The caller is not authenticated, or their credential is no longer good. Answers 401.</summary>
    Unauthorized,

    /// <summary>
    /// The caller is authenticated and is refused for a reason that is safe to state, because it
    /// discloses nothing about another tenant — the setup flow is already complete, two-factor is
    /// already enabled. Answers 403.
    /// </summary>
    /// <remarks>
    /// Never for a resource owned by another account: see <see cref="NotFound"/>.
    /// </remarks>
    Forbidden,

    /// <summary>
    /// Something outside this panel could not be reached or did not answer in time — the ACME
    /// authority, an SMTP relay. Answers 503, which says "try again" rather than "you were wrong".
    /// </summary>
    Unavailable,

    /// <summary>
    /// The panel or the server failed at something the caller could not have influenced, including a
    /// server whose own configuration is incomplete. Answers 500.
    /// </summary>
    Failure,
}
