using Maran.Modules.Licensing.Domain.Enums;

namespace Maran.Modules.Licensing.Models;

/// <summary>The licence service's answer to a lease request, classified; the body is present only for <see cref="LeaseServiceAnswerKind.Received"/>.</summary>
/// <param name="Kind">How the answer is classified.</param>
/// <param name="Body">The response body as text; null unless the kind is <see cref="LeaseServiceAnswerKind.Received"/>.</param>
public sealed record LeaseServiceAnswer(LeaseServiceAnswerKind Kind, string? Body)
{
    /// <summary>An answer that carries no body.</summary>
    /// <param name="kind">Any kind but <see cref="LeaseServiceAnswerKind.Received"/>.</param>
    /// <returns>The answer.</returns>
    public static LeaseServiceAnswer Without(LeaseServiceAnswerKind kind)
    {
        return new LeaseServiceAnswer(kind, null);
    }
}
