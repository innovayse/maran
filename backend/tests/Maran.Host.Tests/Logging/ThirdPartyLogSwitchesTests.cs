using System.Globalization;
using Maran.Host.Logging;
using Maran.Sdk.Contracts;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Maran.Host.Tests.Logging;

/// <summary>
/// Raising the panel's log level never makes a third-party namespace more verbose than its floor
/// (issue #337), while the panel's own namespace keeps following the switch.
/// </summary>
public sealed class ThirdPartyLogSwitchesTests
{
    /// <summary>The panel's own namespace, which no pin covers.</summary>
    private const string PanelContext = "Maran.Modules.Sites.Services.SiteDirectory";

    /// <summary>At Verbose, every pinned third-party namespace still refuses Debug and below.</summary>
    [Theory]
    [InlineData("Wolverine.Runtime.WolverineRuntime")]
    [InlineData("JasperFx.Core.Something")]
    [InlineData("Npgsql.Command")]
    [InlineData("System.Net.Http.HttpClient.agent.ClientHandler")]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Command")]
    [InlineData("Microsoft.AspNetCore.Hosting.Diagnostics")]
    [InlineData("Grpc.Net.Client.Internal.GrpcCall")]
    [InlineData("Polly")]
    public void At_verbose_a_pinned_namespace_still_refuses_debug_and_below(string sourceContext)
    {
        var world = new World(PanelLogLevel.Verbose);

        world.Write(sourceContext, LogEventLevel.Verbose, "trace");
        world.Write(sourceContext, LogEventLevel.Debug, "debug");
        world.Write(sourceContext, LogEventLevel.Information, "information");
        world.Write(sourceContext, LogEventLevel.Warning, "warning");

        Assert.Equal(["warning"], world.Messages);
    }

    /// <summary>The refusing control: the panel's own namespace follows the switch all the way down.</summary>
    [Fact]
    public void The_panels_own_namespace_follows_the_switch_all_the_way_down()
    {
        var world = new World(PanelLogLevel.Verbose);

        world.Write(PanelContext, LogEventLevel.Verbose, "trace");
        world.Write(PanelContext, LogEventLevel.Debug, "debug");

        Assert.Equal(["trace", "debug"], world.Messages);
    }

    /// <summary>Raising the panel's level after the logger was built moves the panel and not the library.</summary>
    [Fact]
    public void Raising_the_level_after_the_logger_was_built_changes_the_panel_and_not_the_library()
    {
        var world = new World(PanelLogLevel.Information);

        world.Write(PanelContext, LogEventLevel.Debug, "before panel");
        world.Write("Npgsql.Command", LogEventLevel.Debug, "before library");
        world.Apply(PanelLogLevel.Debug);
        world.Write(PanelContext, LogEventLevel.Debug, "after panel");
        world.Write("Npgsql.Command", LogEventLevel.Debug, "after library");

        Assert.Equal(["after panel"], world.Messages);
    }

    /// <summary>Choosing Error silences a library's warnings too: the floor is a minimum, not a fixed level.</summary>
    [Fact]
    public void Choosing_error_silences_a_librarys_warnings_too()
    {
        var world = new World(PanelLogLevel.Error);

        world.Write("Npgsql.Command", LogEventLevel.Warning, "warning");
        world.Write("Npgsql.Command", LogEventLevel.Error, "error");

        Assert.Equal(["error"], world.Messages);
    }

    /// <summary>A longer override from configuration cannot make a pinned namespace more verbose.</summary>
    [Fact]
    public void A_longer_override_from_configuration_cannot_make_a_pinned_namespace_more_verbose()
    {
        var world = new World(PanelLogLevel.Verbose, configure: configuration =>
        {
            configuration.MinimumLevel.Override("Npgsql.Command", LogEventLevel.Verbose);
        });

        world.Write("Npgsql.Command", LogEventLevel.Debug, "debug");
        world.Write("Npgsql.Command", LogEventLevel.Warning, "warning");

        Assert.Equal(["warning"], world.Messages);
    }

    /// <summary>Hosting.Lifetime keeps its Information lines inside the pinned Microsoft namespace.</summary>
    [Fact]
    public void The_hosting_lifetime_category_keeps_its_information_lines()
    {
        var world = new World(PanelLogLevel.Verbose);

        world.Write("Microsoft.Hosting.Lifetime", LogEventLevel.Information, "started");
        world.Write("Microsoft.Hosting.Lifetime", LogEventLevel.Debug, "debug");

        Assert.Equal(["started"], world.Messages);
    }

    /// <summary>A name that merely starts with a pinned namespace's letters is not that namespace.</summary>
    [Fact]
    public void A_name_that_merely_starts_with_a_pinned_namespaces_letters_is_not_that_namespace()
    {
        var world = new World(PanelLogLevel.Verbose);

        world.Write("Npgsqlx.Panel", LogEventLevel.Debug, "debug");
        world.Write("Systemic.Panel", LogEventLevel.Debug, "debug too");

        Assert.Equal(["debug", "debug too"], world.Messages);
    }

    /// <summary>An event with no source context is the panel's own and is never refused.</summary>
    [Fact]
    public void An_event_with_no_source_context_is_never_refused()
    {
        var world = new World(PanelLogLevel.Verbose);

        world.WriteWithoutContext(LogEventLevel.Debug, "bare");

        Assert.Equal(["bare"], world.Messages);
    }

    /// <summary>Pinning one namespace twice is refused when the switches are built.</summary>
    [Fact]
    public void Pinning_one_namespace_twice_is_refused()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            return new ThirdPartyLogSwitches(
            [
                new ThirdPartyLogPin("Npgsql", LogEventLevel.Warning),
                new ThirdPartyLogPin("Npgsql", LogEventLevel.Error),
            ]);
        });
    }

    /// <summary>The shipped list names the namespaces issue #337 asks for, each at no lower than Warning.</summary>
    [Fact]
    public void The_shipped_list_pins_the_named_namespaces_at_warning_or_above()
    {
        foreach (var required in new[] { "Wolverine", "Npgsql", "System", "Microsoft" })
        {
            var pin = Assert.Single(ThirdPartyLogPins.All, candidate => { return candidate.Namespace == required; });
            Assert.True(pin.Floor >= LogEventLevel.Warning);
        }
    }

    /// <summary>A logger built the way the host builds it, writing into memory.</summary>
    private sealed class World
    {
        /// <summary>The logger under test.</summary>
        private readonly Logger _logger;

        /// <summary>The panel's window onto the logger's level.</summary>
        private readonly SerilogPanelLogLevelSwitch _panelSwitch;

        /// <summary>The rendered text of every event that reached the sink.</summary>
        private readonly CollectingSink _sink = new();

        /// <summary>The messages written so far, in order.</summary>
        public IReadOnlyList<string> Messages
        {
            get
            {
                return _sink.Messages;
            }
        }

        /// <summary>Builds the logger.</summary>
        /// <param name="level">The panel's level at start.</param>
        /// <param name="configure">Stands in for configuration-file overrides; applied before the pins.</param>
        public World(PanelLogLevel level, Action<LoggerConfiguration>? configure = null)
        {
            var levelSwitch = new LoggingLevelSwitch();
            var thirdParty = new ThirdPartyLogSwitches(ThirdPartyLogPins.All);
            _panelSwitch = new SerilogPanelLogLevelSwitch(levelSwitch, thirdParty);
            _panelSwitch.Apply(level);

            var configuration = new LoggerConfiguration().MinimumLevel.ControlledBy(levelSwitch);
            configure?.Invoke(configuration);
            thirdParty.ApplyTo(configuration);
            _logger = configuration.WriteTo.Sink(_sink).CreateLogger();
        }

        /// <summary>Changes the panel's level, the way the endpoint does.</summary>
        /// <param name="level">The new level.</param>
        public void Apply(PanelLogLevel level)
        {
            _panelSwitch.Apply(level);
        }

        /// <summary>Writes one event as the named source.</summary>
        /// <param name="sourceContext">The writing type's or category's name.</param>
        /// <param name="level">The event's level.</param>
        /// <param name="message">The text.</param>
        public void Write(string sourceContext, LogEventLevel level, string message)
        {
            _logger.ForContext(Constants.SourceContextPropertyName, sourceContext).Write(level, message);
        }

        /// <summary>Writes one event that carries no source context.</summary>
        /// <param name="level">The event's level.</param>
        /// <param name="message">The text.</param>
        public void WriteWithoutContext(LogEventLevel level, string message)
        {
            _logger.Write(level, message);
        }
    }

    /// <summary>Collects the rendered text of every event it receives.</summary>
    private sealed class CollectingSink : ILogEventSink
    {
        /// <summary>Every message received, in order.</summary>
        public List<string> Messages { get; } = [];

        /// <inheritdoc />
        public void Emit(LogEvent logEvent)
        {
            Messages.Add(logEvent.RenderMessage(CultureInfo.InvariantCulture));
        }
    }
}
