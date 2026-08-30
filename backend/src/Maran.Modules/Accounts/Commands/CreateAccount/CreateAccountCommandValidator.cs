using FluentValidation;
using Maran.Modules.Accounts.Persistence;

namespace Maran.Modules.Accounts.Commands.CreateAccount;

/// <summary>
/// Validates <see cref="CreateAccountCommand"/> before it reaches the handler (rules/security.md
/// "Input"). <see cref="CreateAccountCommand.Name"/> is re-validated inside the agent too, once the
/// agent's Accounts operations exist, because the API's validation never substitutes for the
/// agent's own boundary check.
/// </summary>
public sealed class CreateAccountCommandValidator : AbstractValidator<CreateAccountCommand>
{
    /// <summary>
    /// The <c>PlanNotFound</c> machine code, attached to the plan existence rule below so a caller
    /// translating validation failures into typed errors (rather than letting an unknown plan id
    /// reach the database as a foreign-key violation) uses the same code — and therefore the same
    /// translated sentence — the rest of the module does. It names an entry in
    /// <c>Resources/ErrorMessages.resx</c>, which is where that sentence lives.
    /// </summary>
    private const string PlanNotFoundErrorCode = nameof(Resources.ErrorMessages.PlanNotFound);

    /// <summary>The Accounts module's database context, used to confirm a submitted plan id exists.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Configures the field rules for <see cref="CreateAccountCommand"/>.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    public CreateAccountCommandValidator(AccountsDbContext dbContext)
    {
        _dbContext = dbContext;

        // Matches the POSIX portable username character set and a conservative length, so a
        // validated name is always safe to become the account's eventual Linux user name.
        RuleFor(command => command.Name)
            .NotEmpty()
            .MaximumLength(32)
            .Matches("^[a-z][a-z0-9_-]{2,31}$")
            .WithMessage("Name must be a lowercase, Linux-username-safe identifier.");

        RuleFor(command => command.PrimaryDomain)
            .NotEmpty()
            .MaximumLength(253)
            .Matches("^(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\\.(?!-)[A-Za-z0-9-]{1,63}(?<!-))+$")
            .WithMessage("PrimaryDomain must be a valid domain name.");

        RuleFor(command => command.PlanId)
            .NotEmpty()
            .MustAsync(PlanExistsAsync)
            .WithErrorCode(PlanNotFoundErrorCode)
            .WithMessage(command =>
            {
                return $"Plan '{command.PlanId}' was not found.";
            });
    }

    /// <summary>Confirms <paramref name="planId"/> names a real, seeded plan — never a bare foreign-key violation reaching the customer.</summary>
    /// <param name="planId">The plan id submitted on the command.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    private async Task<bool> PlanExistsAsync(Guid planId, CancellationToken cancellationToken)
    {
        return await _dbContext.Plans.AsNoTracking().AnyAsync(p => p.Id == planId, cancellationToken);
    }
}
