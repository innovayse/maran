using FluentValidation;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Interfaces;
using Maran.Modules.Accounts.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;

namespace Maran.Modules.Accounts.Validators;

/// <summary>
/// The rules about what a plan may hold, written once for every command that states one
/// (rules/security.md "Input"). <see cref="Plan"/> repeats the zero-allowance guards as the last line
/// of defence; this is where a caller is told, as a validation failure, instead of by an exception.
/// </summary>
/// <remarks>
/// <para>
/// <b>Disk and sites must be positive</b> although <see cref="Plan"/>'s constructor accepts zero for
/// them. A plan that sells no disk is an account that cannot hold a file, and one that sells no sites
/// is an account that cannot serve one: both are broken plans wearing a working plan's name, for the
/// reason the entity gives for databases and SFTP logins. The entity cannot refuse them without
/// breaking fixtures elsewhere that build plans with arbitrary figures, so the boundary does.
/// </para>
/// <para>
/// <b>The upper bounds are typo and overflow guards, not policy.</b> They are far above anything a
/// server can honour — a disk bound of a pebibyte, in megabytes — and exist so that an extra digit,
/// or a number that would overflow when multiplied into bytes for the agent, is refused here with a
/// sentence rather than reaching the kernel as a nonsense quota.
/// </para>
/// </remarks>
/// <typeparam name="TCommand">The command whose plan fields are validated.</typeparam>
public abstract class PlanDefinitionValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : IPlanDefinition
{
    /// <summary>
    /// The largest disk quota a plan may state, in megabytes: one pebibyte. Multiplied into bytes it
    /// is 2^50, comfortably inside the unsigned 64-bit figure the agent's quota call takes.
    /// </summary>
    private const int MaxDiskQuotaMb = 1_073_741_824;

    /// <summary>The largest figure a plan may state for a count of sites, databases, logins or cron entries.</summary>
    private const int MaxCount = 1_000_000;

    /// <summary>The largest worker budget a plan may state for one php-fpm pool.</summary>
    private const int MaxWorkers = 10_000;

    /// <summary>
    /// The largest CPU quota a plan may state, in percent of one CPU: 1024 whole CPUs. The agent's
    /// own bound (<c>cpu_quota_percent::MAXIMUM</c>); the two must stay equal.
    /// </summary>
    private const int MaxCpuQuotaPercent = 102_400;

    /// <summary>
    /// The largest memory ceiling a plan may state, in megabytes: one pebibyte. The agent's own bound
    /// (<c>memory_max_mib::MAXIMUM</c>); the two must stay equal.
    /// </summary>
    private const int MaxMemoryMb = 1_073_741_824;

    /// <summary>
    /// The largest process ceiling a plan may state: the kernel's <c>PID_MAX_LIMIT</c> on 64-bit hosts.
    /// The agent's own bound (<c>tasks_max::MAXIMUM</c>); the two must stay equal.
    /// </summary>
    private const int MaxProcessCeiling = 4_194_304;

    /// <summary>Configures the field rules every plan-stating command shares.</summary>
    protected PlanDefinitionValidator()
    {
        // Stop at the first failure: the control-character rule reads the text, and a missing name
        // has none. NotEmpty also refuses a name of nothing but white space.
        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(Plan.NameMaxLength)
            .Must(name =>
            {
                return !name.Any(char.IsControl);
            })
            .WithMessage("Name must not contain control characters.");

        RuleFor(command => command.DiskQuotaMb).InclusiveBetween(1, MaxDiskQuotaMb);
        RuleFor(command => command.MaxSites).InclusiveBetween(1, MaxCount);
        RuleFor(command => command.MaxDatabases).InclusiveBetween(1, MaxCount);
        RuleFor(command => command.MaxSftpUsers).InclusiveBetween(1, MaxCount);
        RuleFor(command => command.MaxFtpUsers).InclusiveBetween(0, MaxCount);
        RuleFor(command => command.MaxCronEntries).InclusiveBetween(0, MaxCount);
        RuleFor(command => command.MaxDnsZones)
            .Must(zones =>
            {
                return zones is null || (zones >= 0 && zones <= MaxCount);
            });
        RuleFor(command => command.MaxPhpWorkersPerPool).InclusiveBetween(1, MaxWorkers);

        // Each stated feature must name exactly one feature: the JSON converter also accepts a number
        // or a comma-joined name, which can arrive as a set, as None or as a value nothing defines.
        // The list is required: a request that leaves it out binds to null and is refused here, since
        // no reading of "not stated" exists. An empty list is stated and sells no feature.
        RuleFor(command => command.Features).NotNull();
        RuleForEach(command => command.Features)
            .Must(feature =>
            {
                return feature.IsFeature();
            })
            .WithMessage(nameof(ErrorMessages.PlanFeatureUnknown));

        // A ceiling the caller left out binds to null; NotNull answers that before the child rules
        // read it, so an absent ceiling is refused rather than read as "unlimited".
        RuleFor(command => command.CpuQuotaPercent)
            .NotNull()
            .ChildRules(ceiling =>
            {
                CeilingRules(ceiling, MaxCpuQuotaPercent);
            });
        RuleFor(command => command.MemoryMaxMb)
            .NotNull()
            .ChildRules(ceiling =>
            {
                CeilingRules(ceiling, MaxMemoryMb);
            });
        RuleFor(command => command.MaxProcesses)
            .NotNull()
            .ChildRules(ceiling =>
            {
                CeilingRules(ceiling, MaxProcessCeiling);
            });

        // The PHP bounds are optional: a request that does not state them keeps the default on creation
        // and the current bounds on an edit. Stated, each must be positive and no larger than what the
        // agent will ever write into a pool, so a plan cannot sell a figure the host refuses.
        RuleFor(command => command.PhpLimits!)
            .ChildRules(limits =>
            {
                PhpLimitsRules(limits);
            })
            .When(command =>
            {
                return command.PhpLimits is not null;
            });

        // The mail figures are optional like the PHP bounds: left out, creation sells the default and an
        // edit keeps the current figures. Stated, each must sit in its range and the default quota of a
        // new mailbox must fit the pool, so the value's own guard can never be the first to refuse.
        RuleFor(command => command.MailLimits!)
            .ChildRules(limits =>
            {
                MailLimitsRules(limits);
            })
            .When(command =>
            {
                return command.MailLimits is not null;
            });
    }

    /// <summary>
    /// The rules for stated mail figures, each in the range <see cref="MailLimits.Of"/> accepts, and the
    /// default quota no larger than the pool (a pool already refused for its own range is not judged
    /// again). Child rules rather than a validator class of its own, for the reason
    /// <see cref="CeilingRules"/> gives.
    /// </summary>
    /// <param name="limits">The child validator for the stated figures.</param>
    private static void MailLimitsRules(InlineValidator<MailLimitsDto> limits)
    {
        limits.RuleFor(l => l.MaxMailboxes).InclusiveBetween(0, MailLimits.MaxMailboxesBound);
        limits.RuleFor(l => l.DefaultMailboxQuotaMib).InclusiveBetween(1, MailLimits.MaxQuotaMib);
        limits.RuleFor(l => l.MailboxSendLimitPerHour).InclusiveBetween(1, MailLimits.MaxSendLimitPerHourBound);
        limits.RuleFor(l => l.MailboxQuotaPoolMib).InclusiveBetween(1, MailLimits.MaxQuotaMib);
        limits.RuleFor(l => l.DefaultMailboxQuotaMib)
            .LessThanOrEqualTo(l => l.MailboxQuotaPoolMib)
            .WithMessage(nameof(ErrorMessages.PlanMailLimitsInvalid))
            .When(l =>
            {
                // A pool that is itself out of range is reported on the pool alone, not twice.
                return l.MailboxQuotaPoolMib >= 1;
            });
    }

    /// <summary>
    /// The rules for stated PHP bounds: each from 1 to the agent's own ceiling for that setting
    /// (<see cref="PhpSettingCeilings"/>, kept equal to the agent's table). Child rules rather than a
    /// validator class of its own, for the reason <see cref="CeilingRules"/> gives.
    /// </summary>
    /// <param name="limits">The child validator for the stated bounds.</param>
    private static void PhpLimitsRules(InlineValidator<PhpLimitsDto> limits)
    {
        limits.RuleFor(l => l.MemoryLimitMaxMb).InclusiveBetween(1, PhpSettingCeilings.MaxSizeMb);
        limits.RuleFor(l => l.UploadMaxMb).InclusiveBetween(1, PhpSettingCeilings.MaxSizeMb);
        limits.RuleFor(l => l.MaxExecutionSeconds).InclusiveBetween(1, PhpSettingCeilings.MaxExecutionSeconds);
        limits.RuleFor(l => l.MaxInputVars).InclusiveBetween(1, PhpSettingCeilings.MaxInputVars);
    }

    /// <summary>
    /// The rules for one runtime ceiling: a figure from 1 to the agent's own bound when limited. An
    /// unlimited ceiling has no figure to check, because the wire shape carries none. Child rules
    /// rather than a validator class of its own, because the module registers every validator type
    /// it finds and this one needs its bound.
    /// </summary>
    /// <param name="ceiling">The child validator for the ceiling.</param>
    /// <param name="maximum">The largest figure the agent accepts for this ceiling.</param>
    private static void CeilingRules(InlineValidator<ResourceCeilingDto> ceiling, int maximum)
    {
        ceiling.When(
            c =>
            {
                return c is LimitedResourceCeilingDto;
            },
            () =>
            {
                ceiling.RuleFor(c => ((LimitedResourceCeilingDto)c).Maximum)
                    .InclusiveBetween(1, maximum)
                    .OverridePropertyName(nameof(LimitedResourceCeilingDto.Maximum));
            });
    }
}
