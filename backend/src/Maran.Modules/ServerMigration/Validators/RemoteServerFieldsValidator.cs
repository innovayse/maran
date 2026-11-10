using FluentValidation;
using Maran.Modules.ServerMigration.Domain.Policies;
using Maran.Modules.ServerMigration.Interfaces;
using Maran.Modules.ServerMigration.Resources;

namespace Maran.Modules.ServerMigration.Validators;

/// <summary>
/// Holds the fields of a remote server to the bounds the API accepts (rules/security.md "Input"). The add and the
/// edit command's validators derive from it, so the two can never accept different servers.
/// </summary>
/// <typeparam name="TCommand">The command carrying the fields.</typeparam>
/// <remarks>
/// Each broken rule answers its own code from <c>ValidationMessages</c>, as both the failure's error code and its
/// message — the message is what the panel's exception middleware reads as the code of a 400. What the agent
/// refuses afterwards (a loopback or link-local address, this host itself) is not judged here: it depends on what
/// a name resolves to, which only the agent can ask.
/// </remarks>
public abstract class RemoteServerFieldsValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : IRemoteServerFields
{
    /// <summary>Declares the rules for every field of a remote server.</summary>
    protected RemoteServerFieldsValidator()
    {
        RuleFor(command => command.Name)
            .Must(name =>
            {
                return !string.IsNullOrWhiteSpace(name);
            })
            .WithErrorCode(nameof(ValidationMessages.RemoteServerNameRequired))
            .WithMessage(nameof(ValidationMessages.RemoteServerNameRequired));

        RuleFor(command => command.Name)
            .Must(name =>
            {
                return name is null || name.Trim().Length <= RemoteServerFieldLimits.NameMaxLength;
            })
            .WithErrorCode(nameof(ValidationMessages.RemoteServerNameMaxLength))
            .WithMessage(nameof(ValidationMessages.RemoteServerNameMaxLength));

        RuleFor(command => command.Host)
            .Must(host =>
            {
                return !string.IsNullOrWhiteSpace(host);
            })
            .WithErrorCode(nameof(ValidationMessages.RemoteServerHostRequired))
            .WithMessage(nameof(ValidationMessages.RemoteServerHostRequired));

        RuleFor(command => command.Host)
            .Must(host =>
            {
                return string.IsNullOrWhiteSpace(host) || SourceHostPolicy.IsValid(SourceHostPolicy.Normalize(host));
            })
            .WithErrorCode(nameof(ValidationMessages.RemoteServerHostInvalid))
            .WithMessage(nameof(ValidationMessages.RemoteServerHostInvalid));

        RuleFor(command => command.Port)
            .InclusiveBetween(RemoteServerFieldLimits.PortMinimum, RemoteServerFieldLimits.PortMaximum)
            .WithErrorCode(nameof(ValidationMessages.RemoteServerPortInvalid))
            .WithMessage(nameof(ValidationMessages.RemoteServerPortInvalid));

        RuleFor(command => command.SshUser)
            .Must(user =>
            {
                return user is not null && SourceUserPolicy.IsValid(SourceUserPolicy.Normalize(user));
            })
            .WithErrorCode(nameof(ValidationMessages.RemoteServerSshUserInvalid))
            .WithMessage(nameof(ValidationMessages.RemoteServerSshUserInvalid));

        RuleFor(command => command.AllowedFrom)
            .Must(allowedFrom =>
            {
                return AllowedFromPolicy.TryRead(allowedFrom, out _);
            })
            .WithErrorCode(nameof(ValidationMessages.RemoteServerAllowedFromInvalid))
            .WithMessage(nameof(ValidationMessages.RemoteServerAllowedFromInvalid));
    }
}
