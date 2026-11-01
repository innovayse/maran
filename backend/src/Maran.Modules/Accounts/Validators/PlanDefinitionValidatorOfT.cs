using FluentValidation;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Interfaces;

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
        RuleFor(command => command.MaxPhpWorkersPerPool).InclusiveBetween(1, MaxWorkers);

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
    }

    /// <summary>
    /// The rules for one runtime ceiling: a known kind, a figure from 1 to the agent's own bound when
    /// limited, and no figure at all when unlimited — an unlimited ceiling carrying a figure is refused,
    /// not ignored (one field, one meaning). Child rules rather than a validator class of its own,
    /// because the module registers every validator type it finds and this one needs its bound.
    /// </summary>
    /// <param name="ceiling">The child validator for the ceiling.</param>
    /// <param name="maximum">The largest figure the agent accepts for this ceiling.</param>
    private static void CeilingRules(InlineValidator<ResourceCeilingDto> ceiling, int maximum)
    {
        ceiling.RuleFor(c => c.Kind).IsInEnum();
        ceiling.RuleFor(c => c.Maximum)
            .InclusiveBetween(1, maximum)
            .When(c =>
            {
                return c.Kind == ResourceLimitKind.Limited;
            });
        ceiling.RuleFor(c => c.Maximum)
            .Equal(0)
            .When(c =>
            {
                return c.Kind == ResourceLimitKind.Unlimited;
            });
    }
}
