using FluentValidation;
using Maran.Modules.Marketplace.Domain.Policies;
using Maran.Modules.Marketplace.Resources;

namespace Maran.Modules.Marketplace.Queries.GetModuleDetails;

/// <summary>Validates <see cref="GetModuleDetailsQuery"/>: the route's module id is well formed.</summary>
public sealed class GetModuleDetailsQueryValidator : AbstractValidator<GetModuleDetailsQuery>
{
    /// <summary>Creates the validator.</summary>
    public GetModuleDetailsQueryValidator()
    {
        RuleFor(query => query.Id)
            .Must(ModuleIdPolicy.IsValid)
            .WithMessage(nameof(ErrorMessages.ModuleIdInvalid));
    }
}
