using Maran.Host.Demo.Common;
using Maran.Host.Demo.Guard;
using Maran.Host.Demo.Options;
using Maran.Host.Demo.Resources;
using Maran.Host.Demo.World;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Maran.Host.Demo.Reset;

/// <summary>
/// Builds the answer of <c>GET api/v1/demo/status</c>: the cadence, the last and next reset, the
/// published sign-ins and the reasons shown beside blocked controls.
/// </summary>
/// <remarks>
/// Everything here is public on purpose: the demo's login page prints the sign-ins. Nothing here may
/// ever read a secret of the panel. The reasons are the backend's words in the caller's language, so
/// the SPA renders text and holds none.
/// </remarks>
public sealed class DemoStatusProvider
{
    /// <summary>The role id the SPA expects for the administrator sign-in.</summary>
    public const string AdministratorRole = "administrator";

    /// <summary>The role id the SPA expects for the customer sign-in.</summary>
    public const string CustomerRole = "customer";

    /// <summary>Reads the last reset.</summary>
    private readonly DemoResetMarker _marker;

    /// <summary>Reads the cadence.</summary>
    private readonly DemoOptions _demo;

    /// <summary>Reads the published sign-ins.</summary>
    private readonly DemoIdentityOptions _identities;

    /// <summary>Supplies the reasons in the caller's language.</summary>
    private readonly IStringLocalizer<DemoMessages> _messages;

    /// <summary>Creates the provider.</summary>
    /// <param name="marker">Reads the last reset.</param>
    /// <param name="demo">The demonstration's settings.</param>
    /// <param name="identities">The published sign-ins.</param>
    /// <param name="messages">Supplies the reasons in the caller's language.</param>
    public DemoStatusProvider(
        DemoResetMarker marker,
        IOptions<DemoOptions> demo,
        IOptions<DemoIdentityOptions> identities,
        IStringLocalizer<DemoMessages> messages)
    {
        _marker = marker;
        _demo = demo.Value;
        _identities = identities.Value;
        _messages = messages;
    }

    /// <summary>Builds the status.</summary>
    /// <param name="cancellationToken">Cancelled when the caller gives up.</param>
    /// <returns>The status; both instants are null until a reset has been recorded.</returns>
    public async Task<DemoStatusDto> GetAsync(CancellationToken cancellationToken)
    {
        var last = await _marker.ReadAsync(cancellationToken);
        DateTimeOffset? next = last is { } at ? at.AddHours(_demo.ResetIntervalHours) : null;

        return new DemoStatusDto(
            _demo.ResetIntervalHours,
            last,
            next,
            [
                new DemoIdentityDto(
                    AdministratorRole, _identities.AdministratorUsername, _identities.AdministratorPassword),
                new DemoIdentityDto(
                    CustomerRole, StandardDemoWorld.FeaturedCustomerAccount, _identities.CustomerPassword),
            ],
            DemoRefusalAreaIds.All
                .Select(DemoRefusalAreaIds.IdOf)
                .Select(area => { return new DemoRefusalReasonDto(area, _messages[area].Value); })
                .ToList());
    }
}
