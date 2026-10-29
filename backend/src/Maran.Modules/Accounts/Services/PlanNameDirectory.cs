using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// Answers whether a name is already some other plan's, so creating and renaming a plan apply one
/// rule about what makes two plans indistinguishable.
/// </summary>
/// <remarks>
/// <para>
/// <b>It compares what a person would read, not what is stored.</b> A shipped plan's stored name is a
/// resource key and an operator's is free text, so a comparison of stored columns would let an
/// operator create a second plan called "Starter" beside the shipped one whose key resolves to
/// "Starter" — two plans the customer cannot tell apart. Every plan is therefore compared by the name
/// <see cref="PlanMapper.DisplayNameOf"/> shows in the request's language.
/// </para>
/// <para>
/// <b>Without regard to case</b>, because "Gold" and "gold" are the same word to the customer choosing
/// between them. The database's unique index is exact-match and is the backstop, not the rule.
/// </para>
/// <para>
/// <b>Known limit, stated:</b> a shipped plan's name is compared in the request's language only, so
/// an administrator working in Russian can name a plan "Starter" beside a shipped plan whose English
/// name is "Starter". The two are told apart in every language except English. Closing that would
/// mean resolving every shipped name in every supported culture, which the localizer does not offer.
/// </para>
/// <para>
/// The table is read whole. A server has as many plans as its operator sells — a handful — and
/// resolving names has to happen in memory anyway, because a translated name does not exist in SQL.
/// </para>
/// </remarks>
public sealed class PlanNameDirectory
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Resolves a shipped plan's resource key in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the directory.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="displayNames">Resolves a shipped plan's resource key in the request's culture.</param>
    public PlanNameDirectory(AccountsDbContext dbContext, IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _displayNames = displayNames;
    }

    /// <summary>Reports whether <paramref name="name"/> already belongs to a plan other than the one named.</summary>
    /// <param name="name">The name being claimed, already trimmed.</param>
    /// <param name="exceptPlanId">
    /// The plan that is claiming it, which may of course keep its own name; <c>null</c> for a plan that
    /// does not exist yet.
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><c>true</c> when another plan already reads as <paramref name="name"/>.</returns>
    public async Task<bool> IsTakenAsync(string name, Guid? exceptPlanId, CancellationToken cancellationToken)
    {
        var plans = await _dbContext.Plans.AsNoTracking().ToListAsync(cancellationToken);

        return plans.Any(plan =>
        {
            // The stored key is compared as well as the displayed name: the database's unique index is
            // on the key, so a name equal to a shipped plan's resource key would otherwise pass here
            // and be refused by the index with a constraint violation instead of a sentence.
            return plan.Id != exceptPlanId
                && (string.Equals(PlanMapper.DisplayNameOf(plan, _displayNames), name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(plan.DisplayNameKey, name, StringComparison.OrdinalIgnoreCase));
        });
    }
}
