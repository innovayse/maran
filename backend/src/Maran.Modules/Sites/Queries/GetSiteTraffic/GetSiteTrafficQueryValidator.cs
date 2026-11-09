using FluentValidation;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Resources;

namespace Maran.Modules.Sites.Queries.GetSiteTraffic;

/// <summary>
/// Validates <see cref="GetSiteTrafficQuery"/>: a window, when one is named, must be one the server
/// lists. The window decides how many rows a read touches, so a free number is not accepted.
/// </summary>
public sealed class GetSiteTrafficQueryValidator : AbstractValidator<GetSiteTrafficQuery>
{
    /// <summary>Configures the rules for <see cref="GetSiteTrafficQuery"/>.</summary>
    public GetSiteTrafficQueryValidator()
    {
        RuleFor(query => query.SiteId)
            .NotEmpty();

        RuleFor(query => query.Days)
            .Must(days => { return days is null || TrafficHistory.IsWindow(days.Value); })
            .WithMessage(nameof(ErrorMessages.SiteTrafficWindowInvalid));
    }
}
