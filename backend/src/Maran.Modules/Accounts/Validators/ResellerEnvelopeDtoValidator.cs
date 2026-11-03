using FluentValidation;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Resources;
using Maran.Sdk.Extensions;

namespace Maran.Modules.Accounts.Validators;

/// <summary>
/// The rules about what an envelope may state, written once for every command that carries one
/// (rules/security.md "Input"): creating a reseller and changing its envelope.
/// </summary>
/// <remarks>
/// <para>
/// <b>Zero is allowed for every count and ceiling.</b> An envelope of zero plans sells no plans and an
/// envelope of zero FTPS logins sells none, which an administrator may genuinely choose, and the
/// domain value accepts it. A negative figure never is.
/// </para>
/// <para>
/// <b>The upper bound is an overflow and typo guard, not policy.</b> One pebibyte in megabytes, the
/// bound a plan's own disk quota is held to: far above anything a server can honour, and small enough
/// that the disk pool summed over accounts stays inside a 64-bit figure. The bounds a plan is held to
/// are tighter and are enforced on the plan itself; a ceiling above them is simply never reached, so
/// they are not repeated here.
/// </para>
/// </remarks>
public sealed class ResellerEnvelopeDtoValidator : AbstractValidator<ResellerEnvelopeDto>
{
    /// <summary>The largest figure an envelope may state: one pebibyte in megabytes.</summary>
    private const int MaxFigure = 1_073_741_824;

    /// <summary>
    /// The machine code for a figure outside its bounds, passed as the validation message: the
    /// exception middleware forwards a message that is entirely alphanumeric and resolves it as a resx
    /// key, and discards an English sentence.
    /// </summary>
    private const string EnvelopeInvalidMessage = nameof(ErrorMessages.ResellerEnvelopeInvalid);

    /// <summary>Configures the field rules for <see cref="ResellerEnvelopeDto"/>.</summary>
    public ResellerEnvelopeDtoValidator()
    {
        RuleFor(envelope => envelope.MaxAccounts).InclusiveBetween(0, MaxFigure).WithMessage(EnvelopeInvalidMessage);
        RuleFor(envelope => envelope.DiskPoolMb).InclusiveBetween(0, MaxFigure).WithMessage(EnvelopeInvalidMessage);
        RuleFor(envelope => envelope.MaxPlans).InclusiveBetween(0, MaxFigure).WithMessage(EnvelopeInvalidMessage);
        RuleFor(envelope => envelope.MaxDiskQuotaMb).InclusiveBetween(0, MaxFigure).WithMessage(EnvelopeInvalidMessage);
        RuleFor(envelope => envelope.MaxSites).InclusiveBetween(0, MaxFigure).WithMessage(EnvelopeInvalidMessage);
        RuleFor(envelope => envelope.MaxDatabases).InclusiveBetween(0, MaxFigure).WithMessage(EnvelopeInvalidMessage);
        RuleFor(envelope => envelope.MaxSftpUsers).InclusiveBetween(0, MaxFigure).WithMessage(EnvelopeInvalidMessage);
        RuleFor(envelope => envelope.MaxFtpUsers).InclusiveBetween(0, MaxFigure).WithMessage(EnvelopeInvalidMessage);
        RuleFor(envelope => envelope.MaxCronEntries).InclusiveBetween(0, MaxFigure).WithMessage(EnvelopeInvalidMessage);
        RuleFor(envelope => envelope.MaxPhpWorkersPerPool)
            .InclusiveBetween(0, MaxFigure)
            .WithMessage(EnvelopeInvalidMessage);

        // The list is required: a request that leaves it out binds to null and is refused here, since
        // no reading of "not stated" exists. An empty list is stated and sells no feature. Each member
        // must name exactly one feature, as a plan's own list does.
        RuleFor(envelope => envelope.Features).NotNull().WithMessage(nameof(ErrorMessages.PlanFeatureUnknown));
        RuleForEach(envelope => envelope.Features)
            .Must(feature =>
            {
                return feature.IsFeature();
            })
            .WithMessage(nameof(ErrorMessages.PlanFeatureUnknown));

        // A ceiling the caller left out binds to null; NotNull answers that before the child rules read
        // it, so an absent ceiling is refused rather than read as "unlimited".
        RuleFor(envelope => envelope.CpuQuotaPercent)
            .NotNull()
            .WithMessage(EnvelopeInvalidMessage)
            .ChildRules(CeilingRules);
        RuleFor(envelope => envelope.MemoryMaxMb)
            .NotNull()
            .WithMessage(EnvelopeInvalidMessage)
            .ChildRules(CeilingRules);
        RuleFor(envelope => envelope.MaxProcesses)
            .NotNull()
            .WithMessage(EnvelopeInvalidMessage)
            .ChildRules(CeilingRules);
    }

    /// <summary>
    /// The rules for one runtime ceiling: a figure from 1 to <see cref="MaxFigure"/> when limited. An
    /// unlimited ceiling has no figure to check, because the wire shape carries none.
    /// </summary>
    /// <param name="ceiling">The child validator for the ceiling.</param>
    private static void CeilingRules(InlineValidator<ResourceCeilingDto> ceiling)
    {
        ceiling.When(
            c =>
            {
                return c is LimitedResourceCeilingDto;
            },
            () =>
            {
                ceiling.RuleFor(c => ((LimitedResourceCeilingDto)c).Maximum)
                    .InclusiveBetween(1, MaxFigure)
                    .WithMessage(EnvelopeInvalidMessage)
                    .OverridePropertyName(nameof(LimitedResourceCeilingDto.Maximum));
            });
    }
}
