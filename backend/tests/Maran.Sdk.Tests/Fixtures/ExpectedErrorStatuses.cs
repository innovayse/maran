using Maran.SharedKernel.Results;
using Microsoft.AspNetCore.Http;

namespace Maran.Sdk.Tests.Fixtures;

/// <summary>
/// The <see cref="ErrorType"/> every machine-stable error code of the panel is expected to carry,
/// stated once, explicitly, for every code — never inferred by re-implementing a production rule.
/// </summary>
/// <remarks>
/// <para>
/// This table used to hold a STATUS per code, because the status was inferred from the code's
/// spelling and the inference was the thing at risk. The kind now lives on <see cref="Error"/> and
/// the compiler requires it at every construction site, so what this table records is the other
/// half: the decision, in one reviewable place, of what kind each shipped code IS. A code added to
/// a module's resx and never classified here fails the census test by name.
/// </para>
/// <para>
/// The entries are decisions, not derivations. A table that computed the kind from the suffix would
/// be the old inference written a second time and would agree with any broken version of itself.
/// Where a kind was CHANGED from what the panel used to answer, the change was the point: eighteen
/// codes naming a server failure — <c>AcmeAuthorityUnreachable</c>, <c>DatabaseProvisioningFailed</c>,
/// <c>MailDeliveryFailed</c>, <c>AgentSystemFailure</c> and the rest — were pinned at 400 here,
/// which recorded the panel telling a customer their request was malformed when the server had
/// failed. They are <see cref="ErrorType.Failure"/> and <see cref="ErrorType.Unavailable"/> now.
/// </para>
/// <para>
/// <see cref="AnsweredOutsideResultTranslation"/> holds the codes that never reach
/// <c>ApiResultExtensions</c> at all — the Host writes their responses itself. They are listed so
/// that the census-completeness check can tell "classified as not routed" apart from "nobody has
/// looked at this code yet", which is the distinction the whole test rests on.
/// </para>
/// </remarks>
public static class ExpectedErrorStatuses
{
    /// <summary>Every shipped error code, with the kind of failure it is.</summary>
    public static readonly IReadOnlyDictionary<string, ErrorType> Kinds =
        new Dictionary<string, ErrorType>(StringComparer.Ordinal)
        {
            ["AccountCleanupFailed"] = ErrorType.Failure,
            ["AccountDomainTaken"] = ErrorType.Conflict,
            ["AccountNameTaken"] = ErrorType.Conflict,
            ["AccountNotFound"] = ErrorType.NotFound,
            ["AcmeAccountRejected"] = ErrorType.Failure,
            ["AcmeAuthorityUnreachable"] = ErrorType.Unavailable,
            ["AcmeCertificateUnreadable"] = ErrorType.Failure,
            ["AcmeChallengeTokenInvalid"] = ErrorType.Failure,
            ["AcmeChallengeUnavailable"] = ErrorType.Unavailable,
            ["AcmeChallengeWriteFailed"] = ErrorType.Failure,
            ["AcmeOrderRejected"] = ErrorType.Failure,
            ["AcmeValidationFailed"] = ErrorType.Failure,
            ["AcmeValidationTimedOut"] = ErrorType.Unavailable,
            ["AgentAlreadyExists"] = ErrorType.Conflict,
            ["AgentFirewallPortsMisconfigured"] = ErrorType.Failure,
            ["AgentInvalidInput"] = ErrorType.Validation,
            ["AgentInvalidResponse"] = ErrorType.Failure,
            ["AgentNotFound"] = ErrorType.NotFound,
            ["AgentSystemFailure"] = ErrorType.Failure,
            ["AgentUnspecified"] = ErrorType.Failure,
            ["AgentValidationFailed"] = ErrorType.Validation,
            ["BanAddressInvalid"] = ErrorType.Validation,
            ["BanAddressLoopback"] = ErrorType.Validation,
            ["BanDurationInvalid"] = ErrorType.Validation,
            ["BanNotFound"] = ErrorType.NotFound,
            ["CertificateAlreadyExists"] = ErrorType.Conflict,
            ["CertificateDomainInvalidFormat"] = ErrorType.Validation,
            ["CertificateMaterialInvalid"] = ErrorType.Validation,
            ["CertificateNotFound"] = ErrorType.NotFound,
            ["CronCommandInvalid"] = ErrorType.Validation,
            ["CronEntryAlreadyExists"] = ErrorType.Conflict,
            ["CronEntryIdInvalid"] = ErrorType.Validation,
            ["CronEntryLimitReached"] = ErrorType.Conflict,
            ["CronEntryNotFound"] = ErrorType.NotFound,
            ["CronEnvironmentDuplicateName"] = ErrorType.Validation,
            ["CronEnvironmentInvalid"] = ErrorType.Validation,
            ["CronEnvironmentNameInvalid"] = ErrorType.Validation,
            ["CronEnvironmentNameReserved"] = ErrorType.Validation,
            ["CronEnvironmentTooManyVariables"] = ErrorType.Validation,
            ["CronEnvironmentValueInvalid"] = ErrorType.Validation,
            ["CronOperationFailed"] = ErrorType.Failure,
            ["CronScheduleInvalid"] = ErrorType.Validation,
            ["DatabaseLimitReached"] = ErrorType.Conflict,
            ["DatabaseNameInvalidFormat"] = ErrorType.Validation,
            ["DatabaseNameTaken"] = ErrorType.Conflict,
            ["DatabaseNameTooLong"] = ErrorType.Validation,
            ["DatabaseNotFound"] = ErrorType.NotFound,
            ["DatabaseProvisioningFailed"] = ErrorType.Failure,
            ["DatabaseUserNameInvalidFormat"] = ErrorType.Validation,
            ["DatabaseUserNameTaken"] = ErrorType.Conflict,
            ["DatabaseUserNameTooLong"] = ErrorType.Validation,
            ["EmailInvalidFormat"] = ErrorType.Validation,
            ["EmailTaken"] = ErrorType.Conflict,
            ["HostValidationFailed"] = ErrorType.Validation,
            ["InvalidCredentialsUnauthorized"] = ErrorType.Unauthorized,
            ["InvalidTwoFactorCodeUnauthorized"] = ErrorType.Unauthorized,
            ["MailDeliveryFailed"] = ErrorType.Failure,
            ["MailRecipientInvalid"] = ErrorType.Validation,
            ["PasswordResetEmailInvalid"] = ErrorType.Validation,
            ["PasswordResetTokenInvalid"] = ErrorType.Validation,
            ["PasswordTooWeak"] = ErrorType.Validation,
            ["PhpVersionInvalidFormat"] = ErrorType.Validation,
            ["PhpVersionNotInstalled"] = ErrorType.Validation,
            ["PlanNotFound"] = ErrorType.NotFound,
            ["RefreshTokenInvalidUnauthorized"] = ErrorType.Unauthorized,
            ["RefreshTokenReusedUnauthorized"] = ErrorType.Unauthorized,
            ["RulePortInvalid"] = ErrorType.Validation,
            ["RuleProtocolInvalid"] = ErrorType.Validation,
            ["RuleSourceCidrInvalid"] = ErrorType.Validation,
            ["SecurityPolicyInvalid"] = ErrorType.Validation,
            ["SessionNotFound"] = ErrorType.NotFound,
            ["SetupAlreadyCompletedForbidden"] = ErrorType.Forbidden,
            ["SetupTokenInvalidUnauthorized"] = ErrorType.Unauthorized,
            ["SftpUserLimitReached"] = ErrorType.Conflict,
            ["SftpUserNameInvalidFormat"] = ErrorType.Validation,
            ["SftpUserNameTaken"] = ErrorType.Conflict,
            ["SftpUserNameTooLong"] = ErrorType.Validation,
            ["SftpUserNotFound"] = ErrorType.NotFound,
            ["SftpUserProvisioningFailed"] = ErrorType.Failure,
            ["SiteAliasDuplicated"] = ErrorType.Validation,
            ["SiteAliasInvalidFormat"] = ErrorType.Validation,
            ["SiteBackendNotPhp"] = ErrorType.Validation,
            ["SiteDomainInvalidFormat"] = ErrorType.Validation,
            ["SiteDomainTaken"] = ErrorType.Conflict,
            ["SiteLimitReached"] = ErrorType.Conflict,
            ["SiteLogHistoryLinesInvalid"] = ErrorType.Validation,
            ["SiteLogSourceInvalid"] = ErrorType.Validation,
            ["SiteLogTailFailed"] = ErrorType.Failure,
            ["SiteNotFound"] = ErrorType.NotFound,
            ["SiteProxyUpstreamInvalidFormat"] = ErrorType.Validation,
            ["SmtpAlertRecipientInvalid"] = ErrorType.Validation,
            ["SmtpFromAddressInvalid"] = ErrorType.Validation,
            ["SmtpFromNameInvalid"] = ErrorType.Validation,
            ["SmtpHostInvalid"] = ErrorType.Validation,
            ["SmtpNotConfigured"] = ErrorType.Validation,
            ["SmtpPasswordInvalid"] = ErrorType.Validation,
            ["SmtpPortInvalid"] = ErrorType.Validation,
            ["SmtpSecurityInvalid"] = ErrorType.Validation,
            ["SmtpUsernameInvalid"] = ErrorType.Validation,
            ["TaskAbandonedByRestart"] = ErrorType.Failure,
            ["TaskNotFound"] = ErrorType.NotFound,
            ["TwoFactorAlreadyEnabledForbidden"] = ErrorType.Forbidden,
            ["TwoFactorNotEnabledForbidden"] = ErrorType.Forbidden,
            ["TwoFactorRequiredUnauthorized"] = ErrorType.Unauthorized,
            ["UsernameInvalidFormat"] = ErrorType.Validation,
            ["UsernameTaken"] = ErrorType.Conflict,
            ["UserNotFound"] = ErrorType.NotFound,
            ["WhitelistCidrInvalid"] = ErrorType.Validation,
            ["WhitelistCidrTaken"] = ErrorType.Conflict,
            ["WhitelistEntryNotFound"] = ErrorType.NotFound,
            ["WhitelistEntryProtectsCaller"] = ErrorType.Conflict,
        };

    /// <summary>
    /// Codes the Host answers without going through result translation: the rate limiter's
    /// rejection handler writes 429 itself, and the last-resort exception middleware writes 500.
    /// They are listed here rather than in <see cref="Kinds"/> because pinning them there would
    /// document a status the panel never sends through that path.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> AnsweredOutsideResultTranslation =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["HostRateLimited"] = StatusCodes.Status429TooManyRequests,
            ["HostUnexpectedError"] = StatusCodes.Status500InternalServerError,
        };
}
