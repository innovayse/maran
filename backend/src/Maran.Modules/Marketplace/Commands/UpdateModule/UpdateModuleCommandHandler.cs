using Maran.Modules.Marketplace.Common;
using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Services;

namespace Maran.Modules.Marketplace.Commands.UpdateModule;

/// <summary>
/// Handles <see cref="UpdateModuleCommand"/>: checks the request, takes the module's operation gate and
/// starts the update as a task, answering before the work is done.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order:</b> the acknowledgement; the password (<c>ReauthenticationFailed</c> when wrong, counted toward
/// lockout); a repeat of a update that is still running (answered with its task, nothing created); the
/// verified catalogue entry; <see cref="InstallEligibility"/>; then the gate, the audit entry, the task and
/// the hand-over (<see cref="ModuleOperationStarter"/>). Every refusal on the way is journalled with its
/// reason and changes nothing.
/// </para>
/// <para>
/// Nothing is applied here. The task downloads, has the loader verify and stage the package, and ends with
/// "restart required"; a failure keeps the version that runs.
/// </para>
/// </remarks>
public sealed class UpdateModuleCommandHandler
{
    /// <summary>The operation this handler starts.</summary>
    private const MarketplaceOperation Operation = MarketplaceOperation.Update;

    /// <summary>The front half shared by every operation.</summary>
    private readonly ModuleOperationStarter _starter;

    /// <summary>Finds the version asked for in the verified catalogue.</summary>
    private readonly ModuleOfferResolver _offers;

    /// <summary>The one place eligibility is decided.</summary>
    private readonly InstallEligibility _eligibility;

    /// <summary>Creates the handler.</summary>
    /// <param name="starter">The front half shared by every operation.</param>
    /// <param name="offers">Finds the version asked for in the verified catalogue.</param>
    /// <param name="eligibility">The one place eligibility is decided.</param>
    public UpdateModuleCommandHandler(
        ModuleOperationStarter starter,
        ModuleOfferResolver offers,
        InstallEligibility eligibility)
    {
        _starter = starter;
        _offers = offers;
        _eligibility = eligibility;
    }

    /// <summary>Starts the update.</summary>
    /// <param name="command">The validated command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The task that carries the work, or the coded refusal.</returns>
    public async Task<Result<ModuleOperationAcceptedDto>> HandleAsync(
        UpdateModuleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var asked = new ModulePackageReference(command.Id, command.Version, PackageSha256: null);

        if (!command.Acknowledged)
        {
            await _starter.RecordRefusalAsync(
                Operation, asked, MarketplaceOperationRefusals.NotAcknowledged, command.IpAddress, command.UserAgent, cancellationToken);
            return Result<ModuleOperationAcceptedDto>.Fail(MarketplaceOperationRefusals.NotAcknowledged);
        }

        var verified = await _starter.ReauthenticateAsync(
            Operation, asked, command.UserId, command.Password, command.IpAddress, command.UserAgent, cancellationToken);
        if (!verified.IsSuccess)
        {
            return Result<ModuleOperationAcceptedDto>.Fail(verified.Error!);
        }

        var repeat = await _starter.FindRepeatAsync(Operation, command.Id, command.Version, cancellationToken);
        if (repeat is not null)
        {
            return Result<ModuleOperationAcceptedDto>.Ok(repeat);
        }

        var offer = await _offers.ResolveAsync(command.Id, command.Version, cancellationToken);
        if (!offer.IsSuccess)
        {
            await _starter.RecordRefusalAsync(
                Operation, asked, offer.Error!, command.IpAddress, command.UserAgent, cancellationToken);
            return Result<ModuleOperationAcceptedDto>.Fail(offer.Error!);
        }

        var refusal = await _eligibility.RefuseUpdateAsync(
            offer.Value.Context, offer.Value.Module, offer.Value.Version, cancellationToken);
        if (refusal is not null)
        {
            await _starter.RecordRefusalAsync(
                Operation, asked, refusal, command.IpAddress, command.UserAgent, cancellationToken);
            return Result<ModuleOperationAcceptedDto>.Fail(refusal);
        }

        var package = asked with { PackageSha256 = offer.Value.Version!.Sha256 };
        return await _starter.StartAsync(
            Operation, package, offer.Value.Module.DisplayName, command.UserId, command.IpAddress, command.UserAgent, cancellationToken);
    }
}
