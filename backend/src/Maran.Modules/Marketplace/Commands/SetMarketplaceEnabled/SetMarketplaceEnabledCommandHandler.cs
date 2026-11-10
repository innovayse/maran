using Maran.Modules.Marketplace.Common;
using Maran.Modules.Marketplace.Services;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Marketplace.Commands.SetMarketplaceEnabled;

/// <summary>
/// Handles <see cref="SetMarketplaceEnabledCommand"/>: re-verifies the administrator's password, stores the
/// switch and journals the change.
/// </summary>
/// <remarks>
/// Idempotent: switching on what is already on writes the same row again and journals it again, so every
/// press is on the trail. A wrong password is refused with <c>ReauthenticationFailed</c> (a 400, never a
/// 401, which would sign the administrator out), counts toward the account's lockout, and is journalled as
/// a refused switch change. The switch is the only thing that lets the panel contact the marketplace, so
/// nothing else is changed here and no marketplace call is made.
/// </remarks>
public sealed class SetMarketplaceEnabledCommandHandler
{
    /// <summary>Proves the administrator's password again.</summary>
    private readonly IReauthentication _reauthentication;

    /// <summary>The stored switch.</summary>
    private readonly MarketplaceSettingsStore _settings;

    /// <summary>The audit journal.</summary>
    private readonly MarketplaceAuditJournal _journal;

    /// <summary>The catalogue cache, dropped so a switched-off marketplace shows nothing fetched while it was on.</summary>
    private readonly CatalogueCache _cache;

    /// <summary>Reads the status the command answers with.</summary>
    private readonly MarketplaceStatusReader _status;

    /// <summary>Creates the handler.</summary>
    /// <param name="reauthentication">Proves the administrator's password again.</param>
    /// <param name="settings">The stored switch.</param>
    /// <param name="journal">The audit journal.</param>
    /// <param name="cache">The catalogue cache, dropped when the switch changes.</param>
    /// <param name="status">Reads the status the command answers with.</param>
    public SetMarketplaceEnabledCommandHandler(
        IReauthentication reauthentication,
        MarketplaceSettingsStore settings,
        MarketplaceAuditJournal journal,
        CatalogueCache cache,
        MarketplaceStatusReader status)
    {
        _reauthentication = reauthentication;
        _settings = settings;
        _journal = journal;
        _cache = cache;
        _status = status;
    }

    /// <summary>Applies the switch.</summary>
    /// <param name="command">The validated command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The marketplace status after the change, or the re-authentication failure.</returns>
    public async Task<Result<MarketplaceStatusDto>> HandleAsync(
        SetMarketplaceEnabledCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var verified = await _reauthentication.VerifyAsync(
            command.UserId, command.Password ?? string.Empty, cancellationToken);
        if (!verified.IsSuccess)
        {
            await _journal.RecordSwitchAsync(
                command.Enabled, verified.Error, command.IpAddress, command.UserAgent, cancellationToken);
            return Result<MarketplaceStatusDto>.Fail(verified.Error!);
        }

        if (command.Enabled)
        {
            await _settings.EnableAsync(command.UserId, cancellationToken);
        }
        else
        {
            await _settings.DisableAsync(command.UserId, cancellationToken);
        }

        _cache.Invalidate();
        await _journal.RecordSwitchAsync(
            command.Enabled, failure: null, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<MarketplaceStatusDto>.Ok(await _status.ReadAsync(cancellationToken));
    }
}
