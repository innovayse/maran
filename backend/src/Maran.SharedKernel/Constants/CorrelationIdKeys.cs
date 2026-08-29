namespace Maran.SharedKernel.Constants;

/// <summary>
/// The single definition of how a request's correlation id travels through the process.
/// Lives in the SharedKernel because both the Host (which mints and stores the id) and the
/// Sdk (which reads it when rendering responses) reference this project, and modules must
/// never reference the Host to obtain it.
/// </summary>
public static class CorrelationIdKeys
{
    /// <summary>Key the id is stored under in <c>HttpContext.Items</c>.</summary>
    public const string ItemsKey = "CorrelationId";

    /// <summary>Request/response header carrying the id across process boundaries.</summary>
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>Field name the id is published under in RFC 7807 problem responses and logs.</summary>
    public const string PayloadField = "correlationId";
}
