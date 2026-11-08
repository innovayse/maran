using FluentValidation;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Resources;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.Dns.Commands.SaveDnsSettings;

/// <summary>
/// Validates <see cref="SaveDnsSettingsCommand"/> (rules/security.md "Input"). Every name here is rendered
/// into every zone's SOA and NS records, so each is held to the host-name rule and an address to the strict
/// spelling of <see cref="IpAddressRule"/>. Messages are bare resx keys.
/// </summary>
public sealed class SaveDnsSettingsCommandValidator : AbstractValidator<SaveDnsSettingsCommand>
{
    /// <summary>Configures the field rules for <see cref="SaveDnsSettingsCommand"/>.</summary>
    public SaveDnsSettingsCommandValidator()
    {
        RuleFor(command => command.PrimaryNameserver)
            .Must(IsHostName)
            .WithMessage(nameof(ErrorMessages.DnsNameserverInvalid));

        RuleFor(command => command.SecondaryNameserver)
            .Must(name =>
            {
                return string.IsNullOrEmpty(name) || IsHostName(name);
            })
            .WithMessage(nameof(ErrorMessages.DnsNameserverInvalid));

        RuleFor(command => command.HostmasterMailbox)
            .Must(IsHostName)
            .WithMessage(nameof(ErrorMessages.DnsHostmasterInvalid));

        RuleFor(command => command.PublicIpv4)
            .Must(address =>
            {
                return string.IsNullOrEmpty(address) || IpAddressRule.IsIpv4(address);
            })
            .WithMessage(nameof(ErrorMessages.DnsAddressInvalid));

        RuleFor(command => command.PublicIpv6)
            .Must(address =>
            {
                return string.IsNullOrEmpty(address) || IpAddressRule.IsIpv6(address);
            })
            .WithMessage(nameof(ErrorMessages.DnsAddressInvalid));

        RuleFor(command => command.DefaultTtl)
            .InclusiveBetween(DnsLimits.MinTtl, DnsLimits.MaxTtl)
            .WithMessage(nameof(ErrorMessages.DnsDefaultTtlOutOfRange));
    }

    /// <summary>Tells whether a value is a host name of the panel's strict spelling and a legal length.</summary>
    /// <param name="value">The value.</param>
    /// <returns>True for a host name of at least two labels within the length bound.</returns>
    private static bool IsHostName(string? value)
    {
        return value is not null && value.Length <= DnsLimits.MaxNameLength && HostNameRule.IsHostName(value);
    }
}
