using Maran.Modules.Licensing.Options;
using Maran.Modules.Licensing.Resources;
using Maran.Modules.Licensing.Services;
using Maran.Sdk.Alerts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wolverine;

namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>
/// The renewal worker over its real parts and the real shared alert path, with a journal that can be taken down
/// and a message bus that records what was published. The licence service is the harness's scripted client.
/// </summary>
internal sealed class RenewalWorld : IDisposable
{
    /// <summary>The address the operator alert is mailed to.</summary>
    internal const string OperatorAddress = "ops@example.test";

    /// <summary>The provider the journal and the alerter are resolved from, one scope per report.</summary>
    private readonly ServiceProvider _provider;

    /// <summary>The renewal's real parts on a temporary directory.</summary>
    internal RenewalHarness Harness { get; }

    /// <summary>The journal's writer.</summary>
    internal FlakyAuditWriter Audit { get; }

    /// <summary>Everything published on the bus.</summary>
    internal RecordingMessageBus Bus { get; }

    /// <summary>The worker under test.</summary>
    internal LeaseRenewalWorker Worker { get; }

    /// <summary>Builds the world.</summary>
    /// <param name="installDeadlineSeconds">How long a requested pass may take, gate wait included.</param>
    internal RenewalWorld(int installDeadlineSeconds = 12)
    {
        Harness = new RenewalHarness();
        Audit = new FlakyAuditWriter();
        Bus = new RecordingMessageBus();

        _provider = new ServiceCollection()
            .AddSingleton<IAuditWriter>(Audit)
            .AddSingleton<ICurrentUser>(new FakeCurrentUser(isAdmin: true))
            .AddSingleton<IMessageBus>(Bus)
            .AddSingleton<IClock>(Harness.Clock)
            .AddSingleton<IAlertRecipientDirectory>(StubAlertRecipientDirectory.Of([OperatorAddress]))
            .AddSingleton(AlertText())
            .AddScoped<OperatorAlertDispatcher>()
            .AddScoped<LicensingAuditJournal>()
            .AddScoped<LeaseRenewalAlerter>()
            .BuildServiceProvider();

        Worker = new LeaseRenewalWorker(
            Harness.Renewer,
            _provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<LeaseRenewalWorker>.Instance,
            new OptionsWrapper<LeaseServiceOptions>(
                new LeaseServiceOptions { InstallRequestDeadlineSeconds = installDeadlineSeconds }));
    }

    /// <summary>The alert text, read from the module's embedded resources.</summary>
    /// <returns>The localizer.</returns>
    internal static IStringLocalizer<NotificationMessages> AlertText()
    {
        return new StringLocalizer<NotificationMessages>(
            new ResourceManagerStringLocalizerFactory(
                new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
                NullLoggerFactory.Instance));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _provider.Dispose();
        Harness.Dispose();
    }
}
