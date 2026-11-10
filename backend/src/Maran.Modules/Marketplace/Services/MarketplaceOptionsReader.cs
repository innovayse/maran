using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Options;
using Maran.Modules.Marketplace.Validators;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// The one place the client reads <see cref="MarketplaceOptions"/>. The options are validated lazily, so
/// the first read on a server whose configuration is wrong would throw from deep inside whatever asked;
/// this reader turns that into a coded failure instead, which is what "nothing may throw into the
/// caller" requires of a seam that is only ever reached by an administrator's click.
/// </summary>
/// <remarks>
/// It runs <see cref="MarketplaceOptionsValidator"/> itself on every read as well as catching the
/// exception the options pipeline raises, so the verdict does not depend on whether the host wired the
/// validator into the options factory: a client composed without it is still held to the same rules.
/// </remarks>
public sealed class MarketplaceOptionsReader
{
    /// <summary>The validator applied to every read.</summary>
    private static readonly MarketplaceOptionsValidator Validator = new();

    /// <summary>The live options.</summary>
    private readonly IOptionsMonitor<MarketplaceOptions> _monitor;

    /// <summary>Creates the reader.</summary>
    /// <param name="monitor">The live options.</param>
    public MarketplaceOptionsReader(IOptionsMonitor<MarketplaceOptions> monitor)
    {
        _monitor = monitor;
    }

    /// <summary>Reads the current options.</summary>
    /// <returns>The options, or <c>MarketplaceHostNotConfigured</c> when they do not validate.</returns>
    public Result<MarketplaceOptions> Read()
    {
        MarketplaceOptions options;

        try
        {
            options = _monitor.CurrentValue;
        }
        catch (OptionsValidationException)
        {
            return Result<MarketplaceOptions>.Fail(MarketplaceClientRefusals.HostNotConfigured);
        }

        return Validator.Validate(name: null, options).Succeeded
            ? Result<MarketplaceOptions>.Ok(options)
            : Result<MarketplaceOptions>.Fail(MarketplaceClientRefusals.HostNotConfigured);
    }
}
