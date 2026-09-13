using Maran.Modules.Notifications.Common;
using Maran.Modules.Notifications.Domain.Enums;
using Maran.Modules.Notifications.Persistence;

namespace Maran.Modules.Notifications.Queries.GetSmtpSettings;

/// <summary>Handles <see cref="GetSmtpSettingsQuery"/> by reading the singleton row.</summary>
/// <remarks>
/// <para>
/// <b>The password is not read into the answer, and the read model has nowhere to put it.</b> That
/// is the point of <c>SmtpSettingsDto</c>: the guarantee is structural, so no later edit to this
/// handler can leak a provider credential into a browser, a proxy log or a screenshot
/// (rules/security.md item 8). What the settings screen gets instead is
/// <c>HasPassword</c>, which is the whole of what its form needs to know.
/// </para>
/// <para>
/// <b>A panel with no settings is answered with blank ones, not with a failure.</b> A fresh
/// installation has never configured mail; that is the ordinary state, and answering 4xx would make
/// the settings screen show an error where it should show an empty form.
/// </para>
/// <para>
/// This deliberately does NOT read through <c>SmtpSettingsCache</c>. The cache holds the decrypted
/// password so that sending is possible; the read path has no business touching an object that
/// carries one, and going to the database keeps the two paths' capabilities as different as their
/// purposes.
/// </para>
/// </remarks>
public sealed class GetSmtpSettingsQueryHandler
{
    /// <summary>The module's database context, which owns the settings row.</summary>
    private readonly NotificationsDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public GetSmtpSettingsQueryHandler(NotificationsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Returns the panel's mail settings.</summary>
    /// <param name="query">The (parameterless) read request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The settings, or blank ones when the panel has never had any.</returns>
    public async Task<Result<SmtpSettingsDto>> HandleAsync(
        GetSmtpSettingsQuery query,
        CancellationToken cancellationToken)
    {
        var settings = await _dbContext.SmtpSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == Domain.Entities.SmtpSettings.SingletonId, cancellationToken);

        if (settings is null)
        {
            return Result<SmtpSettingsDto>.Ok(new SmtpSettingsDto(
                string.Empty,
                0,
                SmtpSecurity.StartTls,
                string.Empty,
                HasPassword: false,
                string.Empty,
                string.Empty,
                string.Empty,
                UpdatedAt: null));
        }

        return Result<SmtpSettingsDto>.Ok(new SmtpSettingsDto(
            settings.Host,
            settings.Port,
            settings.Security,
            settings.Username,
            HasPassword: settings.Password.Length > 0,
            settings.FromAddress,
            settings.FromName,
            settings.AlertRecipient,
            settings.UpdatedAt));
    }
}
