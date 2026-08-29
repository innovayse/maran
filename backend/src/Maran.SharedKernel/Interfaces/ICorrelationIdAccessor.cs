namespace Maran.SharedKernel.Interfaces;

/// <summary>
/// Read-only access to the current request's correlation id, kept free of ASP.NET types so
/// modules and SharedKernel code can log or propagate it without depending on <c>HttpContext</c>.
/// The Host-side implementation (<c>Maran.Host.Middleware.CorrelationIdAccessor</c>) reads the
/// value that <c>CorrelationIdMiddleware</c> assigns per request.
/// </summary>
public interface ICorrelationIdAccessor
{
    /// <summary>The correlation id of the active request, or <c>null</c> outside a request context.</summary>
    string? CorrelationId { get; }
}
