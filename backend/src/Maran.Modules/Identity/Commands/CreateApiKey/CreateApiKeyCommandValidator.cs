using FluentValidation;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Resources;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.Identity.Commands.CreateApiKey;

/// <summary>
/// Holds <see cref="CreateApiKeyCommand"/> to the key's bounds (rules/security.md "Input"): the name,
/// the closed scope vocabulary and the address ranges. The expiry is judged by the handler, which holds
/// the panel's clock; the three server-established members are written by the action.
/// </summary>
/// <remarks>Each message is a bare resx key: only a key reaches the caller, localized by the panel.</remarks>
public sealed class CreateApiKeyCommandValidator : AbstractValidator<CreateApiKeyCommand>
{
    /// <summary>Declares the name, scope and address-range rules.</summary>
    public CreateApiKeyCommandValidator()
    {
        RuleFor(command => command.Name)
            .Must(name =>
            {
                return IsValidName(name);
            })
            .WithErrorCode(nameof(ErrorMessages.ApiKeyNameInvalid))
            .WithMessage(nameof(ErrorMessages.ApiKeyNameInvalid));

        RuleFor(command => command.Scopes)
            .Must(scopes =>
            {
                return scopes is { Count: > 0 } && scopes.All(scope =>
                {
                    return scope is not null && ApiKeyScopes.All.Contains(scope);
                });
            })
            .WithErrorCode(nameof(ErrorMessages.ApiKeyScopeUnknown))
            .WithMessage(nameof(ErrorMessages.ApiKeyScopeUnknown));

        RuleFor(command => command.AllowedCidrs)
            .Must(cidrs =>
            {
                return AreValidCidrs(cidrs!);
            })
            .WithErrorCode(nameof(ErrorMessages.ApiKeyCidrInvalid))
            .WithMessage(nameof(ErrorMessages.ApiKeyCidrInvalid))
            .When(command =>
            {
                return command.AllowedCidrs is not null;
            });
    }

    /// <summary>Whether a name is 1 to <see cref="ApiKeyLimits.MaxNameLength"/> characters after trimming, with no control character.</summary>
    /// <param name="name">The name as the caller sent it.</param>
    /// <returns>True when the name may label a key.</returns>
    private static bool IsValidName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;

        return trimmed.Length is > 0 and <= ApiKeyLimits.MaxNameLength && !trimmed.Any(char.IsControl);
    }

    /// <summary>
    /// Whether the list holds at least one and at most <see cref="ApiKeyLimits.MaxCidrs"/> ranges, each strictly valid and
    /// binding something. An EMPTY list is refused: <c>null</c> is how a request says "not bound", and an empty one
    /// is a malformed request, not a second spelling of it.
    /// </summary>
    /// <param name="cidrs">The ranges as the caller sent them.</param>
    /// <returns>True when the list may be stored on a key.</returns>
    private static bool AreValidCidrs(IReadOnlyList<string> cidrs)
    {
        return cidrs.Count is > 0 and <= ApiKeyLimits.MaxCidrs && cidrs.All(text =>
        {
            return CidrRule.TryParse(text, out var rule) && rule!.PrefixLength > 0;
        });
    }
}
