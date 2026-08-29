using Maran.SharedKernel.Constants;

namespace Maran.Host.Middleware;

/// <summary>
/// Host-side implementation of <see cref="ICorrelationIdAccessor"/>. Reads the value that
/// <see cref="CorrelationIdMiddleware"/> stored on the current request, via
/// <see cref="IHttpContextAccessor"/> so it can be injected into code (e.g. Wolverine handlers)
/// that runs outside a controller and has no direct <c>HttpContext</c>.
/// </summary>
public sealed class CorrelationIdAccessor : ICorrelationIdAccessor
{
    /// <summary>Provides access to the ambient <see cref="HttpContext"/>, when one exists.</summary>
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Creates the accessor.</summary>
    /// <param name="httpContextAccessor">Provides access to the ambient <see cref="HttpContext"/>.</param>
    public CorrelationIdAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc/>
    public string? CorrelationId
    {
        get
        {
            return _httpContextAccessor.HttpContext?.Items.TryGetValue(CorrelationIdKeys.ItemsKey, out var value) == true
            ? value as string
            : null;
        }
    }
}
