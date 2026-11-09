using Maran.Agent.Client.Services.PhpService;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Mappers;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Resources;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites.Commands.ReplacePhpSettings;

/// <summary>
/// Handles <see cref="ReplacePhpSettingsCommand"/>: checks the customer's settings against the plan,
/// rewrites every pool the account has, and only then stores them (issue #138).
/// </summary>
/// <remarks>
/// <para>
/// <b>Agent first, row second</b>, the order every handler in this module uses: a row saying a setting
/// is on while the pool does not carry it is the panel stating something the host does not keep. The
/// pools are rewritten with the settings cut to the plan's bound; the rows keep what the customer
/// asked for. A failure part-way (the second version refuses) puts the first back
/// (<see cref="PhpPoolApplier"/>) and the row is not touched.
/// </para>
/// <para>
/// <b>A value above the plan is refused here, not clamped.</b> Clamping happens only to a value that
/// WAS inside the bound and is not any more because the plan was edited; a customer typing a figure
/// the plan does not sell is told so (<c>PhpSettingAbovePlan</c>) rather than saving a number that
/// silently runs as another.
/// </para>
/// <para>
/// <b>Another customer's account answers 404.</b> The account is found through the tenant-scoped
/// directory, and the id is the caller's own, stamped by the action.
/// </para>
/// </remarks>
public sealed class ReplacePhpSettingsCommandHandler
{
    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>The account's system user, worker budget and plan bounds.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Which versions the account has pools at.</summary>
    private readonly PhpAccountPoolSettings _pools;

    /// <summary>Rewrites those pools.</summary>
    private readonly PhpPoolApplier _applier;

    /// <summary>Builds the answer.</summary>
    private readonly PhpSettingsReader _reader;

    /// <summary>This module's audit journal.</summary>
    private readonly SiteAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="accounts">The account's system user, worker budget and plan bounds.</param>
    /// <param name="pools">Which versions the account has pools at.</param>
    /// <param name="applier">Rewrites those pools.</param>
    /// <param name="reader">Builds the answer.</param>
    /// <param name="journal">This module's audit journal.</param>
    public ReplacePhpSettingsCommandHandler(
        SitesDbContext dbContext,
        IAccountDirectory accounts,
        PhpAccountPoolSettings pools,
        PhpPoolApplier applier,
        PhpSettingsReader reader,
        SiteAuditJournal journal)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _pools = pools;
        _applier = applier;
        _reader = reader;
        _journal = journal;
    }

    /// <summary>Replaces the account's settings, or refuses the request whole.</summary>
    /// <param name="command">The validated settings; see <see cref="ReplacePhpSettingsCommandValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The settings as they now stand, <c>AccountNotFound</c>, <c>PhpSettingAbovePlan</c>, or the
    /// agent's own typed failure for the first pool it refused.
    /// </returns>
    /// <exception cref="DbUpdateException">
    /// Rethrown when the rows cannot be written after the pools were, once the previous settings have
    /// been sent back. An infrastructure fault, not an answer about the request (rules/csharp.md "Errors").
    /// </exception>
    public async Task<Result<PhpSettingsDto>> HandleAsync(
        ReplacePhpSettingsCommand command,
        CancellationToken cancellationToken)
    {
        var account = await _accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return await FailAsync(
                command, command.AccountId.ToString("D"), Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        if (command.Settings.Any(setting => { return ExceedsPlan(setting, account.PhpLimits); }))
        {
            return await FailAsync(
                command, account.Username, Error.Of(nameof(ErrorMessages.PhpSettingAbovePlan), ErrorType.Validation), cancellationToken);
        }

        var rows = await _dbContext.PhpSettings
            .Where(setting => setting.AccountId == account.Id)
            .ToListAsync(cancellationToken);
        var wanted = command.Settings.ToDictionary(
            setting => { return setting.Name; },
            setting => { return PhpSettingSpelling.ToIni(PhpSettingCatalogue.KindOf(setting.Name), setting.Value); },
            StringComparer.Ordinal);

        var changed = ChangedNames(rows, wanted);
        var previous = PhpSettingMapper.ToAgent(PhpSettingsPlanClamp.Apply(rows, account.PhpLimits));
        var desired = PhpSettingMapper.ToAgent(PhpSettingsPlanClamp.Apply(
            wanted.Select(pair => { return new PhpSetting(Guid.NewGuid(), account.Id, pair.Key, pair.Value); }),
            account.PhpLimits));

        // Nothing to say to the host when the pools would carry exactly what they carry: an edit that
        // only moves a value which the plan's bound already cuts, or a repeated request.
        if (!SameSettings(previous, desired))
        {
            var versions = await _pools.VersionsInUseAsync(account.Id, cancellationToken);
            var applied = await _applier.ApplyAsync(
                account.Username, account.MaxPhpWorkersPerPool, versions, desired, previous, cancellationToken);
            if (!applied.IsSuccess)
            {
                return await FailAsync(command, account.Username, applied.Error!, cancellationToken);
            }
        }

        Store(rows, wanted, account.Id);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The pools already carry the new settings and the rows do not. Put the pools back before
            // the fault surfaces, so the host never keeps a setting the panel did not store.
            var versions = await _pools.VersionsInUseAsync(account.Id, CancellationToken.None);
            await _applier.ApplyAsync(
                account.Username, account.MaxPhpWorkersPerPool, versions, desired: previous, previous: null, CancellationToken.None);
            throw;
        }

        await _journal.RecordPhpSettingsChangedAsync(
            account.Username, changed, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<PhpSettingsDto>.Ok(await _reader.ReadAsync(account, cancellationToken));
    }

    /// <summary>Whether a stated value is above the plan's bound for its setting.</summary>
    /// <param name="setting">A validated setting.</param>
    /// <param name="limits">The plan's bounds.</param>
    /// <returns>True when the setting has a bound and the value is above it.</returns>
    private static bool ExceedsPlan(PhpSettingInputDto setting, PhpLimitsSnapshot limits)
    {
        var maximum = PhpSettingCatalogue.PlanMaximum(setting.Name, limits);

        return maximum is not null
            && PhpSettingSpelling.TryReadNumber(setting.Value, out var number)
            && number > maximum;
    }

    /// <summary>The names whose stored value is added, changed or removed by the request.</summary>
    /// <param name="rows">The account's stored settings.</param>
    /// <param name="wanted">The settings the request keeps, name to php.ini value.</param>
    /// <returns>The changed names in the order of the catalogue.</returns>
    private static List<string> ChangedNames(
        IReadOnlyList<PhpSetting> rows,
        Dictionary<string, string> wanted)
    {
        var stored = rows.ToDictionary(row => { return row.Name; }, row => { return row.Value; }, StringComparer.Ordinal);

        return PhpSettingNames.All
            .Where(name =>
            {
                var had = stored.TryGetValue(name, out var before);
                var has = wanted.TryGetValue(name, out var after);

                return had != has || before != after;
            })
            .ToList();
    }

    /// <summary>Whether two lists carry the same settings in the same order.</summary>
    /// <param name="left">The first list.</param>
    /// <param name="right">The second list.</param>
    /// <returns>True when every name and value is equal.</returns>
    private static bool SameSettings(
        IReadOnlyList<PhpSettingDto> left,
        IReadOnlyList<PhpSettingDto> right)
    {
        return left.SequenceEqual(right);
    }

    /// <summary>Makes the account's rows say exactly what the request keeps.</summary>
    /// <param name="rows">The account's tracked rows.</param>
    /// <param name="wanted">The settings the request keeps, name to php.ini value.</param>
    /// <param name="accountId">The account.</param>
    private void Store(List<PhpSetting> rows, Dictionary<string, string> wanted, Guid accountId)
    {
        foreach (var row in rows.Where(row => { return !wanted.ContainsKey(row.Name); }))
        {
            _dbContext.PhpSettings.Remove(row);
        }

        foreach (var (name, value) in wanted)
        {
            var row = rows.Find(existing => { return existing.Name == name; });
            if (row is null)
            {
                _dbContext.PhpSettings.Add(new PhpSetting(Guid.NewGuid(), accountId, name, value));
            }
            else if (row.Value != value)
            {
                row.ChangeValue(value);
            }
        }
    }

    /// <summary>Journals a refused replacement and returns it as the typed failure.</summary>
    /// <param name="command">The request that was refused.</param>
    /// <param name="subject">The account's system user name, or the supplied identifier when none was found.</param>
    /// <param name="error">The typed failure to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<PhpSettingsDto>> FailAsync(
        ReplacePhpSettingsCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            SiteAuditActions.PhpSettingsChanged, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<PhpSettingsDto>.Fail(error);
    }
}
