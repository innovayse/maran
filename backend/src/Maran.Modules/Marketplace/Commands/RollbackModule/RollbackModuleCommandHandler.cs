using Maran.Modules.Marketplace.Common;
using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Marketplace.Commands.RollbackModule;

/// <summary>
/// Handles <see cref="RollbackModuleCommand"/>: checks the request, takes the module's operation gate and
/// starts the rollback as a task, answering before the work is done.
/// </summary>
/// <remarks>
/// Needs no marketplace and works with the switch off: the module is read from the loader and the ledger.
/// The password comes first; a module the loader does not list, or lists as removed, is
/// <c>ModuleNotInstalled</c> (404); a repeat of the same rollback still running is answered with its task;
/// another operation on the module is <c>ModuleOperationInProgress</c> (409). Every refusal is journalled
/// and changes nothing.
/// </remarks>
public sealed class RollbackModuleCommandHandler
{
    /// <summary>The operation this handler starts.</summary>
    private const MarketplaceOperation Operation = MarketplaceOperation.Rollback;

    /// <summary>The front half shared by every operation.</summary>
    private readonly ModuleOperationStarter _starter;

    /// <summary>The installed modules.</summary>
    private readonly InstalledModuleReader _installed;

    /// <summary>Creates the handler.</summary>
    /// <param name="starter">The front half shared by every operation.</param>
    /// <param name="installed">The installed modules.</param>
    public RollbackModuleCommandHandler(ModuleOperationStarter starter, InstalledModuleReader installed)
    {
        _starter = starter;
        _installed = installed;
    }

    /// <summary>Starts the rollback.</summary>
    /// <param name="command">The validated command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The task that carries the work, or the coded refusal.</returns>
    public async Task<Result<ModuleOperationAcceptedDto>> HandleAsync(
        RollbackModuleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var asked = new ModulePackageReference(command.Id, Version: null, PackageSha256: null);

        var verified = await _starter.ReauthenticateAsync(
            Operation, asked, command.UserId, command.Password, command.IpAddress, command.UserAgent, cancellationToken);
        if (!verified.IsSuccess)
        {
            return Result<ModuleOperationAcceptedDto>.Fail(verified.Error!);
        }

        var facts = (await _installed.ReadAsync(cancellationToken))
            .FirstOrDefault(candidate => { return candidate.Loaded.ModuleId == command.Id; });
        if (facts is null || facts.Loaded.State == ModuleLifecycleState.Removed)
        {
            await _starter.RecordRefusalAsync(
                Operation, asked, MarketplaceOperationRefusals.ModuleNotInstalled, command.IpAddress, command.UserAgent, cancellationToken);
            return Result<ModuleOperationAcceptedDto>.Fail(MarketplaceOperationRefusals.ModuleNotInstalled);
        }

        if (facts.Package?.PreviousVersion is null)
        {
            await _starter.RecordRefusalAsync(
                Operation,
                new ModulePackageReference(command.Id, facts.Loaded.Version, facts.Package?.PackageSha256),
                MarketplaceOperationRefusals.NoPreviousVersion,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);
            return Result<ModuleOperationAcceptedDto>.Fail(MarketplaceOperationRefusals.NoPreviousVersion);
        }

        var repeat = await _starter.FindRepeatAsync(Operation, command.Id, facts.Loaded.Version, cancellationToken);
        if (repeat is not null)
        {
            return Result<ModuleOperationAcceptedDto>.Ok(repeat);
        }

        var package = new ModulePackageReference(command.Id, facts.Loaded.Version, facts.Package?.PackageSha256);
        return await _starter.StartAsync(
            Operation, package, null, command.UserId, command.IpAddress, command.UserAgent, cancellationToken);
    }
}
