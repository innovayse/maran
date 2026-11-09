using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Mappers;

/// <summary>
/// Restates a plan's <see cref="MailLimits"/> in the three shapes it travels in: the wire a screen
/// reads and sends, the stored value, and the Sdk snapshot other modules read (issue #177).
/// </summary>
/// <remarks>
/// It translates and never decides (rules/csharp.md "A mapper translates; it never decides"): whether
/// a request states the figures at all is the handler's question, and whether a figure is in range is
/// the validator's and the value's. Each method carries four figures across unchanged.
/// </remarks>
public static class MailLimitsMapper
{
    /// <summary>Projects the figures onto the wire.</summary>
    /// <param name="limits">The plan's mail figures.</param>
    /// <returns>Their wire shape.</returns>
    public static MailLimitsDto ToDto(MailLimits limits)
    {
        return new MailLimitsDto(
            limits.MaxMailboxes,
            limits.DefaultMailboxQuotaMib,
            limits.MailboxSendLimitPerHour,
            limits.MailboxQuotaPoolMib);
    }

    /// <summary>Reads validated wire figures as the stored value.</summary>
    /// <param name="dto">Figures that passed <see cref="Validators.PlanDefinitionValidator{TCommand}"/>.</param>
    /// <returns>The plan's mail figures.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a figure the validator would have refused.</exception>
    public static MailLimits ToDomain(MailLimitsDto dto)
    {
        return MailLimits.Of(
            dto.MaxMailboxes,
            dto.DefaultMailboxQuotaMib,
            dto.MailboxSendLimitPerHour,
            dto.MailboxQuotaPoolMib);
    }

    /// <summary>Restates the figures as the snapshot other modules read.</summary>
    /// <param name="limits">The plan's mail figures.</param>
    /// <returns>The Sdk snapshot.</returns>
    public static MailLimitsSnapshot ToSnapshot(MailLimits limits)
    {
        return new MailLimitsSnapshot(
            limits.MaxMailboxes,
            limits.DefaultMailboxQuotaMib,
            limits.MailboxSendLimitPerHour,
            limits.MailboxQuotaPoolMib);
    }
}
