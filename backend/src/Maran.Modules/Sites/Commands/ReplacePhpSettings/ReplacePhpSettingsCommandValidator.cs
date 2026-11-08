using FluentValidation;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Resources;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Sites.Commands.ReplacePhpSettings;

/// <summary>
/// Validates <see cref="ReplacePhpSettingsCommand"/> before it reaches the handler: the names are the
/// whitelist's, each value has the shape and the absolute bound of its kind, and the two sizes that
/// must travel together do (issue #138).
/// </summary>
/// <remarks>
/// <para>
/// <b>Advice, not the boundary.</b> The agent re-validates every name and value against its own table
/// (rules/security.md item 1); this is the panel telling a customer why, in their language, before the
/// save. Messages are bare resx keys, for the reason <see cref="CreateSite.CreateSiteCommandValidator"/>
/// gives.
/// </para>
/// <para>
/// <b>The plan's bound is not checked here.</b> It depends on the account, which the validator does not
/// read; the handler refuses a value above it as <c>PhpSettingAbovePlan</c>.
/// </para>
/// <para>
/// <b>The two sizes are set together.</b> A request body smaller than the file it carries makes PHP
/// discard the file without an error, and a setting left unset falls back to the pool's own default,
/// which the panel does not know (it is the agent's). So <c>upload_max_filesize</c> and
/// <c>post_max_size</c> are both stated or neither is, and the post size is not below the upload size —
/// the same rule the agent enforces, stated without copying a default.
/// </para>
/// </remarks>
public sealed class ReplacePhpSettingsCommandValidator : AbstractValidator<ReplacePhpSettingsCommand>
{
    /// <summary>Configures the rules for <see cref="ReplacePhpSettingsCommand"/>.</summary>
    public ReplacePhpSettingsCommandValidator()
    {
        RuleFor(command => command.Settings)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage(nameof(ErrorMessages.PhpSettingsRequired))
            .Must(settings =>
            {
                return settings.All(setting =>
                {
                    return setting is not null && setting.Name is not null && PhpSettingCatalogue.IsKnown(setting.Name);
                });
            })
            .WithMessage(nameof(ErrorMessages.PhpSettingNotAllowed))
            .Must(settings =>
            {
                return settings.Select(setting => { return setting.Name; }).Distinct(StringComparer.Ordinal).Count() == settings.Count;
            })
            .WithMessage(nameof(ErrorMessages.PhpSettingDuplicated))
            .Must(settings =>
            {
                return settings.All(setting =>
                {
                    return setting.Value is not null && HasValidValue(setting);
                });
            })
            .WithMessage(nameof(ErrorMessages.PhpSettingValueInvalid))
            .Must(SizesTravelTogether)
            .WithMessage(nameof(ErrorMessages.PhpSettingSizesTogether))
            .Must(PostSizeIsNotBelowUpload)
            .WithMessage(nameof(ErrorMessages.PhpSettingPostSizeBelowUpload));
    }

    /// <summary>Whether a value has the shape and the absolute bound of its setting's kind.</summary>
    /// <param name="setting">A setting whose name is on the list.</param>
    /// <returns>True when the value could be written to a pool.</returns>
    private static bool HasValidValue(PhpSettingInputDto setting)
    {
        var kind = PhpSettingCatalogue.KindOf(setting.Name);
        if (kind == PhpSettingKind.Timezone)
        {
            return PhpSettingSpelling.IsTimezoneShape(setting.Value);
        }

        return PhpSettingSpelling.TryReadNumber(setting.Value, out var number)
            && number >= 1
            && number <= PhpSettingCatalogue.AbsoluteMaximum(setting.Name);
    }

    /// <summary>Whether the upload size and the post size are both stated or both left out.</summary>
    /// <param name="settings">The validated list.</param>
    /// <returns>True when neither or both are present.</returns>
    private static bool SizesTravelTogether(IReadOnlyList<PhpSettingInputDto> settings)
    {
        return Has(settings, PhpSettingNames.UploadMaxFilesize) == Has(settings, PhpSettingNames.PostMaxSize);
    }

    /// <summary>Whether the post size, when both sizes are stated, is at least the upload size.</summary>
    /// <param name="settings">The validated list.</param>
    /// <returns>True when the rule holds or does not apply.</returns>
    private static bool PostSizeIsNotBelowUpload(IReadOnlyList<PhpSettingInputDto> settings)
    {
        if (!Has(settings, PhpSettingNames.UploadMaxFilesize) || !Has(settings, PhpSettingNames.PostMaxSize))
        {
            return true;
        }

        return Number(settings, PhpSettingNames.PostMaxSize) >= Number(settings, PhpSettingNames.UploadMaxFilesize);
    }

    /// <summary>Whether the list states a setting.</summary>
    /// <param name="settings">The validated list.</param>
    /// <param name="name">The setting's name.</param>
    /// <returns>True when it is present.</returns>
    private static bool Has(IReadOnlyList<PhpSettingInputDto> settings, string name)
    {
        return settings.Any(setting => { return setting.Name == name; });
    }

    /// <summary>The number a stated size setting holds.</summary>
    /// <param name="settings">The validated list, in which the setting is present and its value valid.</param>
    /// <param name="name">The setting's name.</param>
    /// <returns>The figure in mebibytes.</returns>
    private static int Number(IReadOnlyList<PhpSettingInputDto> settings, string name)
    {
        PhpSettingSpelling.TryReadNumber(settings.First(setting => { return setting.Name == name; }).Value, out var number);
        return number;
    }
}
