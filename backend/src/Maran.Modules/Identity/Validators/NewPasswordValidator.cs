using FluentValidation;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;

namespace Maran.Modules.Identity.Validators;

/// <summary>
/// The panel's one rule for the LENGTH of a password a person chooses for themselves after setup: no
/// longer than <see cref="MaxPasswordLength"/> (<c>PasswordTooLong</c>), then not empty and at least
/// the minimum length the operator's security policy names (<c>PasswordTooWeak</c>). Shared through <c>SetValidator</c> by every command that sets a
/// password — reset, invitation and change — so the three cannot drift into accepting different
/// passwords.
/// </summary>
/// <remarks>
/// <para>
/// The minimum is read from <see cref="SecurityPolicyCache"/>, asynchronously, on every validation:
/// an operator who raises it raises it on the next request, with nothing recompiled. The ceiling is a
/// constant because it is not policy but a bound on the work Argon2id is asked to do for one request.
/// </para>
/// <para>
/// The two failures have two codes because they ask for opposite things: a password that is too
/// short is told the operator's minimum, which the rule carries as the <c>MinLength</c> placeholder
/// the Host hands to the text as <c>{1}</c> — the sentence names the length the policy requires, not
/// a length written into the resource — while one over the ceiling is told to shorten it, and a
/// sentence asking for "at least N" there would send the person the wrong way. The ceiling is
/// checked first and stops the chain, so a too-long password gets only its own code. The Host
/// forwards a validation message only when it is a bare resource key.
/// </para>
/// <para>
/// This rule sees the password alone, so it cannot refuse a password equal to the username. That
/// half of the policy (<see cref="PasswordPolicy.DiffersFromUsername"/>) is applied where the
/// username is known: <c>CompleteSetupCommandValidator</c> for the first administrator and the
/// handlers of the other three paths — <c>ChangePasswordCommandHandler</c> for a signed-in change,
/// <c>ResetPasswordCommandHandler</c> and <c>AcceptInvitationCommandHandler</c> for the two token
/// paths. The token paths apply it only AFTER the token has been validated: before that, a refusal
/// that depends on the account the token names would tell an unauthenticated caller the token is
/// real; once the token is valid the refusal reveals nothing the holder does not already have.
/// </para>
/// </remarks>
public sealed class NewPasswordValidator : AbstractValidator<string>
{
    /// <summary>Longest password accepted, bounding the work Argon2id is asked to do.</summary>
    public const int MaxPasswordLength = PasswordPolicy.MaxLength;

    /// <summary>The placeholder name carrying the minimum length, read by the Host as <c>{1}</c>.</summary>
    public const string MinimumLengthPlaceholder = "MinLength";

    /// <summary>Configures the password rule.</summary>
    /// <param name="policyCache">The panel's security policy, read for the minimum password length.</param>
    public NewPasswordValidator(SecurityPolicyCache policyCache)
    {
        RuleFor(password => password)
            .Cascade(CascadeMode.Stop)
            .Must(PasswordPolicy.IsWithinMaximumLength)
            .WithMessage(nameof(ErrorMessages.PasswordTooLong))
            .MustAsync(async (_, password, context, cancellationToken) =>
            {
                var policy = await policyCache.GetAsync(cancellationToken);
                context.MessageFormatter.AppendArgument(MinimumLengthPlaceholder, policy.MinimumPasswordLength);
                return PasswordPolicy.MeetsMinimumLength(password, policy.MinimumPasswordLength);
            })
            .WithMessage(nameof(ErrorMessages.PasswordTooWeak));
    }
}
