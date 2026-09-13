using FluentValidation;
using Maran.Modules.Identity.Services;

namespace Maran.Modules.Identity.Commands.ResetPassword;

/// <summary>
/// Bounds the fields of <see cref="ResetPasswordCommand"/>, reading the password's minimum length
/// from the panel's operator-configurable security policy rather than from a constant.
/// </summary>
/// <remarks>
/// <para>
/// The length rule is asynchronous because the policy is a cached database row (R12). That is the
/// point of the cache: an operator who raises the minimum length raises it here, on the next
/// request, with nothing recompiled and nothing restarted.
/// </para>
/// <para>
/// The token is bounded but not otherwise checked. Whether it is a real token is the handler's
/// question, and a validator that answered it would answer it with a DIFFERENT status code than the
/// handler's refusal — which is exactly the distinction a caller must not be able to draw between a
/// token that never existed and one that has expired.
/// </para>
/// </remarks>
public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    /// <summary>Longest token the panel will even hash, bounding the work an anonymous caller can ask for.</summary>
    private const int MaxTokenLength = 128;

    /// <summary>Longest password accepted, bounding the work Argon2id is asked to do.</summary>
    private const int MaxPasswordLength = 256;

    /// <summary>Configures the field rules for <see cref="ResetPasswordCommand"/>.</summary>
    /// <param name="policyCache">The panel's security policy, read for the minimum password length.</param>
    public ResetPasswordCommandValidator(SecurityPolicyCache policyCache)
    {
        RuleFor(command => command.Token)
            .NotEmpty().WithMessage(nameof(Resources.ErrorMessages.PasswordResetTokenInvalid))
            .MaximumLength(MaxTokenLength).WithMessage(nameof(Resources.ErrorMessages.PasswordResetTokenInvalid));

        RuleFor(command => command.NewPassword)
            .NotEmpty().WithMessage(nameof(Resources.ErrorMessages.PasswordTooWeak))
            .MaximumLength(MaxPasswordLength).WithMessage(nameof(Resources.ErrorMessages.PasswordTooWeak))
            .MustAsync(async (password, cancellationToken) =>
            {
                var policy = await policyCache.GetAsync(cancellationToken);
                return password is not null && password.Length >= policy.MinimumPasswordLength;
            })
            .WithMessage(nameof(Resources.ErrorMessages.PasswordTooWeak));
    }
}
