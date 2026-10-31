namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Every request shape the performance budget measures, per paged list: the first page, the deepest
/// page, a search, each filter, and each sort field in both directions — the shapes the SPA actually
/// sends — plus the two filter vocabularies read off tables that grow with the accounts, over the scale dataset (<see cref="ScaleDataset"/>, <see cref="ScaleJournalDataset"/>).
/// </summary>
/// <remarks>
/// The deep page is computed from the dataset's own counts, so it is the LAST page at
/// <see cref="PageSize"/> — the one whose offset scan is longest — and moves by itself when a count
/// does. Filters name values the dataset is known to hold, so every shape selects real rows rather
/// than measuring an empty answer.
/// </remarks>
public static class ScaleListShapes
{
    /// <summary>The page size every shape asks for: the SPA's default.</summary>
    public const int PageSize = 25;

    /// <summary>The lists measured, by the name each test case carries.</summary>
    public static readonly string[] Lists =
    [
        "accounts", "accountChoices", "plans", "planChoices", "sites", "certificates", "databases",
        "sftpUsers", "ftpUsers", "backups", "backupDestinations", "audit", "tasks", "sessions",
        "bans", "whitelist", "accountsDisk", "auditActions", "taskKinds",
    ];

    /// <summary>A window start one day after the dataset's epoch, as the query string carries it.</summary>
    private static readonly string From = Uri.EscapeDataString(ScaleDataset.Epoch.AddDays(1).ToString("O"));

    /// <summary>A window end three days after the dataset's epoch, as the query string carries it.</summary>
    private static readonly string To = Uri.EscapeDataString(ScaleDataset.Epoch.AddDays(3).ToString("O"));

    /// <summary>The shapes of one list.</summary>
    /// <param name="list">One of <see cref="Lists"/>.</param>
    /// <returns>
    /// Each shape's name, its path with query string, and — for the first page only — the fewest rows
    /// the answer's total may report, which is what the dataset seeded for that list.
    /// </returns>
    public static IReadOnlyList<(string Name, string Path, int? MinimumTotal)> Of(string list)
    {
        var accounts = ScaleDataset.AccountCount;
        var firstAccount = ScaleDataset.IdOf(2, 0);
        return list switch
        {
            "accounts" => Paged("/api/v1/accounts", accounts, "acct05", ["name", "createdAt", "status"],
                [$"planId={ScaleDataset.IdOf(1, 0)}", "status=suspended"]),
            "accountChoices" => Paged("/api/v1/accounts/choices", accounts, "acct05", ["name"], []),
            "plans" => Paged("/api/v1/plans", ScaleDataset.PlanCount, "plan 1", ["name", "diskQuota", "accounts"], []),
            "planChoices" => Paged("/api/v1/plans/choices", ScaleDataset.PlanCount, "plan 1", ["name"], []),
            "sites" => Paged("/api/v1/sites", accounts * ScaleDataset.SitesPerAccount, "acct05",
                ["domain", "createdAt", "status"],
                [$"accountId={firstAccount}", "backendType=php", "phpVersion=8.2", "status=enabled"]),
            "certificates" => Paged("/api/v1/certificates", accounts * ScaleDataset.SitesPerAccount, "acct05",
                ["notAfter", "domain"],
                [$"siteId={ScaleDataset.IdOf(3, 0)}", "status=valid", "status=expired", "expiring=true"]),
            "databases" => Paged("/api/v1/databases", accounts * ScaleDataset.DatabasesPerAccount, "acct05",
                ["name", "createdAt"], [$"accountId={firstAccount}"]),
            "sftpUsers" => Paged("/api/v1/sftp-users", accounts, "acct05", ["name", "createdAt"],
                [$"accountId={firstAccount}"]),
            "ftpUsers" => Paged("/api/v1/ftp-users", accounts, "acct05", ["name", "createdAt"],
                [$"accountId={firstAccount}"]),
            "backups" => Paged("/api/v1/backups", accounts * ScaleJournalDataset.BackupsPerAccount, "acct05",
                ["startedAt", "size"],
                [$"accountId={firstAccount}", "status=failed", "kind=scheduled", $"from={From}&to={To}"]),
            "backupDestinations" => Paged("/api/v1/backup-destinations", ScaleJournalDataset.DestinationCount,
                "Destination 1", ["name", "createdAt"], ["kind=local"]),
            "audit" => Paged("/api/v1/audit", accounts * ScaleJournalDataset.AuditEventsPerAccount, "acct05",
                ["occurredAt", "actor", "action"],
                ["action=SiteCreated", "outcome=failed", $"from={From}&to={To}"]),
            "tasks" => Paged("/api/v1/tasks", accounts * ScaleJournalDataset.TasksPerAccount, "acct05",
                ["startedAt", "subject"],
                [$"accountId={firstAccount}", "status=failed", "kind=BackupCreate", $"from={From}&to={To}"]),
            "sessions" => Paged("/api/v1/sessions", ScaleJournalDataset.SessionCount, "198.51.100.1",
                ["issuedAt", "expiresAt"], []),
            "bans" => Paged("/api/v1/firewall/bans", ScaleJournalDataset.BanCount, "203.0.1",
                ["address", "bannedAt", "expiresAt"], []),
            "whitelist" => Paged("/api/v1/firewall/whitelist", ScaleJournalDataset.WhitelistCount, "10.0.1",
                ["cidr", "createdAt"], []),
            "accountsDisk" => Paged("/api/v1/monitoring/accounts-disk", accounts, "acct05",
                ["username", "quotaBytes"], []),
            // Not paged — each answers a closed vocabulary (the audit actions and the TaskKinds the code
            // writes) — but each reads it off a table that grows with the accounts, so the read is
            // measured like a list.
            "auditActions" => [("whole vocabulary", "/api/v1/audit/actions", null)],
            "taskKinds" => [("whole vocabulary", "/api/v1/tasks/kinds", null)],
            _ => throw new ArgumentOutOfRangeException(nameof(list), list, "No such measured list."),
        };
    }

    /// <summary>Builds the standard shapes of one paged list.</summary>
    /// <param name="path">The list's route.</param>
    /// <param name="rows">How many rows the dataset holds for it, which fixes the deepest page.</param>
    /// <param name="search">A search term the dataset matches.</param>
    /// <param name="sorts">Every sort field the list offers.</param>
    /// <param name="filters">One query-string fragment per filter shape.</param>
    /// <returns>The shapes, named for what they ask.</returns>
    private static List<(string Name, string Path, int? MinimumTotal)> Paged(
        string path,
        int rows,
        string search,
        string[] sorts,
        string[] filters)
    {
        var deepPage = Math.Max(1, (rows + PageSize - 1) / PageSize);
        var shapes = new List<(string Name, string Path, int? MinimumTotal)>
        {
            ("first page", $"{path}?page=1&pageSize={PageSize}", rows),
            ($"deep page {deepPage}", $"{path}?page={deepPage}&pageSize={PageSize}", null),
            ($"search '{search}'", $"{path}?search={Uri.EscapeDataString(search)}&pageSize={PageSize}", null),
        };

        foreach (var filter in filters)
        {
            shapes.Add(($"filter {filter}", $"{path}?{filter}&pageSize={PageSize}", null));
        }

        foreach (var sort in sorts)
        {
            shapes.Add(($"sort {sort}", $"{path}?sort={sort}&pageSize={PageSize}", null));
            shapes.Add(($"sort -{sort}", $"{path}?sort=-{sort}&pageSize={PageSize}", null));
        }

        return shapes;
    }
}
