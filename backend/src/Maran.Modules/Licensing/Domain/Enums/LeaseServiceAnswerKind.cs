namespace Maran.Modules.Licensing.Domain.Enums;

/// <summary>How the licence service's answer to a lease request is classified, before anything judges its content.</summary>
public enum LeaseServiceAnswerKind
{
    /// <summary>A 200 answer whose body fitted the size bound; the body is in the answer.</summary>
    Received,

    /// <summary>The configured address is not a usable https origin, so no request was made.</summary>
    NotConfigured,

    /// <summary>No answer: the connection failed, the request timed out, or the service answered 408, 429 or a 5xx.</summary>
    Unreachable,

    /// <summary>The service answered with another status: it will not issue a lease.</summary>
    Refused,

    /// <summary>The body was larger than the bound and was discarded.</summary>
    TooLarge,
}
