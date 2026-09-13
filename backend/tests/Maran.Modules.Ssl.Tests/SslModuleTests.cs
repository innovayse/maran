using Maran.Modules.Ssl.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Ssl.Tests;

/// <summary>What the module's own registrations promise about configuration, at boot rather than at first use.</summary>
public sealed class SslModuleTests
{
    private static ServiceProvider Provider(string contactEmail)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Panel"] = "Host=localhost;Database=maran;Username=panel",
                ["Acme:ContactEmail"] = contactEmail,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        new SslModule().ConfigureServices(services, configuration);
        return services.BuildServiceProvider();
    }

    /// <summary>A configured contact address that is not one bare address stops the panel booting.</summary>
    /// <remarks>
    /// The wiring half of the check, and the half a validator's own unit tests cannot see: a
    /// perfectly correct AcmeOptionsValidator that nobody registered validates nothing. The
    /// display-name form is the value the <c>[EmailAddress]</c> annotation this replaces accepted.
    /// </remarks>
    [Fact]
    public void A_configured_contact_address_that_is_not_one_bare_address_stops_the_boot()
    {
        using var provider = Provider("Ops Team <ops@example.com>");

        Assert.Throws<OptionsValidationException>(() =>
        {
            return provider.GetRequiredService<IOptions<AcmeOptions>>().Value;
        });
    }

    /// <summary>An ordinary configured contact address binds and passes.</summary>
    [Fact]
    public void An_ordinary_configured_contact_address_binds_and_passes()
    {
        using var provider = Provider("ops@example.com");

        Assert.Equal("ops@example.com", provider.GetRequiredService<IOptions<AcmeOptions>>().Value.ContactEmail);
    }
}
