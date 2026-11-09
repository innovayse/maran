using Maran.Agent.Client.Services.PhpService;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Queries.ListPhpExtensions;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Sites.Tests.Queries.ListPhpExtensions;

/// <summary>Behavioral contract of <see cref="ListPhpExtensionsQueryHandler"/>.</summary>
public sealed class ListPhpExtensionsQueryHandlerTests
{
    /// <summary>An administrator is told the name, label, state and what the host can do with each extension.</summary>
    [Fact]
    public async Task An_administrator_is_told_what_the_host_can_do_with_each_extension()
    {
        var php = Host();

        var result = await Handler(php, FakeCurrentUser.Admin()).HandleAsync(Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var gd = result.Value.Single(extension => { return extension.Name == "gd"; });
        Assert.Equal("Images (GD)", gd.Label);
        Assert.False(gd.Enabled);
        Assert.Equal(ExtensionAvailability.Available, gd.Availability);
        Assert.Equal("Can be switched on and off", gd.AvailabilityName);
        Assert.Equal(
            ExtensionAvailability.NotPackaged,
            result.Value.Single(extension => { return extension.Name == "redis"; }).Availability);
    }

    /// <summary>A customer is told name, label and state, and never the availability.</summary>
    [Fact]
    public async Task A_customer_is_told_name_label_and_state_but_never_the_availability()
    {
        var php = Host();

        var result = await Handler(php, FakeCurrentUser.Customer(Guid.NewGuid())).HandleAsync(Query(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);
        Assert.All(result.Value, extension =>
        {
            Assert.Null(extension.Availability);
            Assert.Null(extension.AvailabilityName);
            Assert.False(string.IsNullOrWhiteSpace(extension.Label));
        });
        Assert.True(result.Value.Single(extension => { return extension.Name == "opcache"; }).Enabled);
    }

    /// <summary>No customer-visible property of an extension is a package name or path.</summary>
    [Fact]
    public async Task The_outward_extension_carries_only_name_label_state_and_availability()
    {
        var result = await Handler(Host(), FakeCurrentUser.Admin()).HandleAsync(Query(), CancellationToken.None);

        var properties = result.Value[0].GetType().GetProperties().Select(property =>
        {
            return property.Name;
        });
        Assert.Equal(["Name", "Label", "Enabled", "Availability", "AvailabilityName"], properties);
    }

    /// <summary>A name outside the panel's catalogue is dropped rather than shown without a label.</summary>
    [Fact]
    public async Task A_name_outside_the_catalogue_is_dropped()
    {
        var php = Host();
        php.Extensions.Add(new PhpExtensionDto("xdebug", true, PhpExtensionAvailability.Available));

        var result = await Handler(php, FakeCurrentUser.Admin()).HandleAsync(Query(), CancellationToken.None);

        Assert.DoesNotContain(result.Value, extension => { return extension.Name == "xdebug"; });
        Assert.Equal(3, result.Value.Count);
    }

    /// <summary>An agent that cannot answer is a failure with its own code, not an empty list.</summary>
    [Fact]
    public async Task An_agent_that_cannot_answer_is_a_failure_not_an_empty_list()
    {
        var php = new RecordingAgentPhpClient(Error.Of("AgentUnavailable", ErrorType.Unavailable));

        var result = await Handler(php, FakeCurrentUser.Admin()).HandleAsync(Query(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
    }

    /// <summary>A host with version 8.3 that loads opcache and could switch gd and could not install redis.</summary>
    private static RecordingAgentPhpClient Host()
    {
        var php = new RecordingAgentPhpClient("8.3");
        php.Extensions.AddRange(
        [
            new PhpExtensionDto("gd", false, PhpExtensionAvailability.Available),
            new PhpExtensionDto("opcache", true, PhpExtensionAvailability.Available),
            new PhpExtensionDto("redis", false, PhpExtensionAvailability.NotPackaged),
        ]);

        return php;
    }

    /// <summary>Builds the handler over the real display names and the given caller.</summary>
    /// <param name="php">The agent's PHP surface.</param>
    /// <param name="caller">Who asks.</param>
    private static ListPhpExtensionsQueryHandler Handler(RecordingAgentPhpClient php, FakeCurrentUser caller)
    {
        return new ListPhpExtensionsQueryHandler(php, caller, SitesTestContext.SiteNames());
    }

    /// <summary>Builds the query for version 8.3.</summary>
    private static ListPhpExtensionsQuery Query()
    {
        return new ListPhpExtensionsQuery("8.3");
    }
}
