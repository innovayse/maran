using Microsoft.Extensions.Options;

namespace Maran.Host.Demo.Options;

/// <summary>Checks the relations between <see cref="DemoOptions"/> values that data annotations cannot.</summary>
public sealed class DemoOptionsValidator : IValidateOptions<DemoOptions>
{
    /// <summary>Validates the options.</summary>
    /// <param name="name">The options name; unused.</param>
    /// <param name="options">The bound options.</param>
    /// <returns>Success, or a failure naming the relation that does not hold.</returns>
    public ValidateOptionsResult Validate(string? name, DemoOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.VisitorMutationsPerWindow > options.GlobalMutationBudget)
        {
            return ValidateOptionsResult.Fail(
                "Demo:VisitorMutationsPerWindow must not exceed Demo:GlobalMutationBudget: one visitor "
                + "could otherwise be granted more than the whole demo has.");
        }

        if (options.VisitorSignInsPerWindow > options.GlobalSignInsPerWindow)
        {
            return ValidateOptionsResult.Fail(
                "Demo:VisitorSignInsPerWindow must not exceed Demo:GlobalSignInsPerWindow: one visitor "
                + "could otherwise be granted more sign-ins than the whole demo accepts.");
        }

        return ValidateOptionsResult.Success;
    }
}
