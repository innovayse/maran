using Maran.Modules.Backups.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Maran.Host.Tests.Composition;

/// <summary>
/// That the panel's unattended passes are actually REGISTERED — the half no module's own test suite
/// can see.
/// </summary>
/// <remarks>
/// <para>
/// A module may not register its own hosted service: a module references only the Sdk and the
/// SharedKernel, and the schedule is Host composition
/// (<c>Maran.Host/Extensions/BackgroundWorkExtensions.cs</c>). The consequence is a split that has
/// already bitten once as a note in a report: a reconciler class can be written, doc-commented, and
/// fully unit-tested inside its module while the one line that ever runs it is missing — and every
/// test in that module passes, because the class under test is constructed directly. Nothing in the
/// module's project could tell the difference.
/// </para>
/// <para>
/// <see cref="HostedServiceResolutionTests"/> beside this file asks whether every registered hosted
/// service RESOLVES. That is a different question and is satisfied by a shorter list: it asserts the
/// collection is not empty, so a registration that had been deleted would leave it passing. This
/// asserts that a named pass is among them.
/// </para>
/// </remarks>
public sealed class BackgroundWorkRegistrationTests : IClassFixture<ValidatingPanelTestFactory>
{
    /// <summary>The host booted with a real boot's container validation.</summary>
    private readonly ValidatingPanelTestFactory _factory;

    /// <summary>Captures the validating host factory.</summary>
    /// <param name="factory">The booted host.</param>
    public BackgroundWorkRegistrationTests(ValidatingPanelTestFactory factory)
    {
        _factory = factory;
    }

    /// <summary>The backup reclamation pass is registered and resolves from the root provider.</summary>
    /// <remarks>
    /// <para>
    /// Without this line in <c>BackgroundWorkExtensions</c> a backup whose stream the panel lost stays
    /// <c>Running</c> for ever, and a Running row refuses every restore of that account — so one lost
    /// stream costs the customer the ability to restore from any of their good backups. That is the
    /// "looks handled" outcome: the class exists, the notes describe a reclamation, nothing reclaims.
    /// </para>
    /// <para>
    /// From the ROOT provider, because that is where the host resolves hosted services; resolving
    /// inside a scope would succeed for a singleton that captured a scoped dependency and prove
    /// nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_backup_reclamation_pass_is_registered_as_a_hosted_service()
    {
        var hosted = _factory.Services.GetServices<IHostedService>().ToList();

        Assert.Contains(hosted, service =>
        {
            return service is StartupBackupReconciler;
        });
    }
}
