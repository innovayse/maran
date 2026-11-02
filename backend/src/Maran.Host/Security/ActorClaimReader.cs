using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json;
using Maran.Sdk.Contracts;

namespace Maran.Host.Security;

/// <summary>
/// Reads a token's view-as markers: whether it is a view-as token at all, and the operator its
/// <c>act</c> claim names (issue #317).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two questions, answered so that a malformed token restricts rather than frees.</b>
/// <see cref="IsViewAs"/> answers "must this token be treated as a view-as one" and is true when
/// EITHER marker is present, whatever it holds. <see cref="TryRead"/> answers "who is the operator",
/// and fails for anything that is not exactly the object the issuer writes. A token that is marked
/// but unreadable is therefore a view-as token with no readable actor, which the bearer handler
/// refuses outright (<c>ViewAsSessionValidation</c>).
/// </para>
/// <para>
/// <b>The claim's wire shape.</b> The issuer writes <c>act</c> as a nested JSON object; the JWT
/// handler hands it back as one claim whose value is that object's raw JSON text. It is parsed here
/// with <see cref="JsonDocument"/> and nothing else, so no type from the token is ever bound.
/// </para>
/// </remarks>
public static class ActorClaimReader
{
    /// <summary>Reports whether a principal's token carries either view-as marker.</summary>
    /// <param name="principal">The validated principal of the request.</param>
    /// <returns>True when the token has an <c>act</c> claim or a session-kind claim.</returns>
    public static bool IsViewAs(ClaimsPrincipal principal)
    {
        return principal.HasClaim(claim =>
        {
            return claim.Type == PanelClaimTypes.Actor || claim.Type == PanelClaimTypes.SessionKind;
        });
    }

    /// <summary>Reads the operator out of the <c>act</c> claim.</summary>
    /// <param name="principal">The validated principal of the request.</param>
    /// <param name="actor">The operator, when the claim is present and well formed.</param>
    /// <returns>True only for an object carrying a user id, a login name and a session id.</returns>
    public static bool TryRead(ClaimsPrincipal principal, [NotNullWhen(true)] out ActorClaim? actor)
    {
        actor = null;
        var raw = principal.FindFirst(PanelClaimTypes.Actor)?.Value;
        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryText(root, PanelClaimTypes.UserId, out var userIdText)
                || !TryText(root, PanelClaimTypes.Username, out var username)
                || !TryText(root, PanelClaimTypes.SessionId, out var sessionIdText)
                || !Guid.TryParse(userIdText, out var userId)
                || !Guid.TryParse(sessionIdText, out var sessionId))
            {
                return false;
            }

            actor = new ActorClaim(userId, username, sessionId);
            return true;
        }
        catch (JsonException)
        {
            // Not JSON at all: unreadable, which the caller treats as a refusal, never as "no actor".
            return false;
        }
    }

    /// <summary>Reads one non-empty string property of the actor object.</summary>
    /// <param name="root">The actor object.</param>
    /// <param name="name">The property name.</param>
    /// <param name="value">Its text, when it is a non-empty string.</param>
    /// <returns>True when the property exists and is a non-empty string.</returns>
    private static bool TryText(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return value.Length > 0;
    }
}
