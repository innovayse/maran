//! The closed set of commands the agent ever asks a registered source to run.

use maran_agent_core::validation::db::remote_database_name::RemoteDatabaseName;
use maran_agent_core::validation::fs::remote_path::RemotePath;
use maran_agent_core::validation::system::source_user_name::SourceUserName;

/// What every remote command starts with: a search path that reaches the
/// administrator tools a non-interactive login does not list, and the C locale
/// the parsers were written against. A constant, so it can carry nothing from a
/// caller.
const PRELUDE: &str =
    "PATH=\"$PATH:/usr/local/sbin:/usr/sbin:/sbin\"; export PATH; LC_ALL=C; export LC_ALL; ";

/// Every command the agent can ask a registered source to run, and nothing else.
///
/// `ssh` hands a command STRING to the remote shell, which is the one place
/// this codebase builds a command line (rules/security.md item 3). It is
/// reconciled here, and this enum is the reconciliation: a CLOSED set of
/// operations, each rendered by [`Self::render`] from a constant string with
/// typed holes. A hole is a [`RemotePath`], a [`SourceUserName`] or a [`RemoteDatabaseName`] and nothing
/// else, both allow-list grammars that contain no character a shell treats
/// specially (no quote, space, `$`, backtick, `;`, `|`, `&`, `<`, `>`, `(`, `)`,
/// `*`, `?`, newline or leading `-`); each hole is also wrapped in single
/// quotes, so a grammar widened by mistake still has to produce a quote to get
/// out. No rpc takes a command, a flag or a path template, no caller supplies
/// any text of this table, and a new operation is a new variant in the same
/// change that adds it to the threat note.
///
/// Every command is READ-ONLY on the source: it lists, reads or measures. None
/// writes, removes, signals or starts anything (decision D15: a generic Linux
/// source is never written to).
///
/// **Where the table lives, and why not the `distro` crate.** The paths below
/// (`/etc/nginx`, `/etc/passwd`, `/home`) are facts about the SOURCE, a host the
/// administrator registered and whose family this agent does not know and does
/// not branch on; they are not a guess about THIS host, which is what the
/// platform-literal rule of `scripts/lib/check-structure.sh` (check 17) and
/// rules/rust.md "Distro adapter" protect. The adapter answers questions about
/// the family the agent runs on, so the source's layout would have no family to
/// be keyed by. The table therefore lives with the one type that renders it.
/// Each command lists the directories of BOTH families a source may run
/// (Debian-style `sites-enabled`, RHEL-style `conf.d`), as one constant script.
///
/// Every command is wrapped the same way: the prelude above, then the body in a
/// group whose standard error is discarded, so what a remote tool complains
/// about can reach neither this host's log nor an error value.
///
/// The documented output of each variant is what its parser in the inventory or
/// the probes expects; the text is UNTRUSTED and every parser bounds it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum RemoteCommand {
    /// Probe. `id -u`: one line, the effective user id in decimal.
    EffectiveUid,
    /// Probe. The filesystem that holds `/home`: one line `<fragment size>
    /// <total blocks> <blocks available to the user>`, all decimal (`stat -f`).
    HomeFilesystemUsage,
    /// Probe. The `PRETTY_NAME=` line of the source's os-release file, or
    /// nothing.
    OsRelease,
    /// Probe. The first database client or server that answers `--version`:
    /// one line of its banner, which says `MariaDB` for MariaDB.
    DatabaseServerVersion,
    /// Probe. The banner of the first web server found, `nginx` before
    /// Apache: `nginx version: nginx/<v>` or `Server version: Apache/<v>`.
    WebServerVersion,
    /// Probe. One line, `yes` when a mail daemon is installed or a virtual
    /// mail tree exists, else `no`.
    MailStorePresent,
    /// Probe. The `VERSION=` line of a Maran panel's release state file, or
    /// nothing on a host that is not one.
    MaranPanelVersion,
    /// Inventory. The host's account database, `getent passwd` format.
    ListPasswd,
    /// Inventory. One path per line: the regular files and links directly in
    /// the nginx include directories of both families.
    ListNginxVhostFiles,
    /// Inventory. The text of one file the previous listing (or an include of
    /// one of those files) named.
    ReadNginxVhostFile {
        /// The file to print.
        path: RemotePath,
    },
    /// Inventory. One path per line: the regular files and links directly in
    /// the Apache site directories of both families.
    ListApacheVhostFiles,
    /// Inventory. The text of one file the previous listing named.
    ReadApacheVhostFile {
        /// The file to print.
        path: RemotePath,
    },
    /// Inventory. One tab-separated line per database: `<name>\t<size bytes>`,
    /// without the server's own system schemas. Run as the login's identity,
    /// which needs the database administrator's access for the full list.
    ListDatabases,
    /// Inventory. The crontab of one user, as `crontab -u <user> -l` prints it.
    ReadCrontab {
        /// The account whose crontab is printed.
        user: SourceUserName,
    },
    /// Inventory. One path per line, one per PHP installation directory:
    /// `/etc/php/<major.minor>` or `/etc/opt/remi/php<majorminor>`.
    ListPhpVersions,
    /// Inventory. `du -skx` of one home: `<kibibytes>\t<path>`.
    HomeDiskUsage {
        /// The home directory to measure.
        home: RemotePath,
    },
    /// Inventory. A decimal count of the Maildir mailboxes (`cur` directories)
    /// found within three levels of `<home>/Maildir` and `<home>/mail`.
    CountMailboxes {
        /// The home directory to look in.
        home: RemotePath,
    },
    /// Transfer. The text of one database as `mysqldump` (or `mariadb-dump`)
    /// prints it, tables and data in one transaction, with no triggers, routines
    /// or events and with binary values as hexadecimal. Read-only on the source.
    DumpDatabase {
        /// The database to dump.
        database: RemoteDatabaseName,
    },
}

impl RemoteCommand {
    /// The text handed to the remote shell for this command.
    ///
    /// The result is the prelude, then a brace group holding the constant body of
    /// the variant with its holes filled by validated values, then the discard of
    /// the group's standard error. Nothing outside the table and the typed holes
    /// is in it.
    #[must_use]
    pub fn render(&self) -> String {
        let body = match self {
            Self::EffectiveUid => "id -u".to_owned(),
            Self::HomeFilesystemUsage => "stat -f -c '%S %b %a' /home".to_owned(),
            Self::OsRelease => "grep -m 1 '^PRETTY_NAME=' /etc/os-release".to_owned(),
            Self::DatabaseServerVersion => {
                "mysql --version || mariadb --version || mysqld --version".to_owned()
            }
            Self::WebServerVersion => concat!(
                "if command -v nginx >/dev/null 2>&1; then nginx -v 2>&1; ",
                "elif command -v apache2 >/dev/null 2>&1; then apache2 -v 2>&1; ",
                "elif command -v httpd >/dev/null 2>&1; then httpd -v 2>&1; fi"
            )
            .to_owned(),
            Self::MailStorePresent => concat!(
                "if command -v postfix >/dev/null 2>&1 || command -v dovecot >/dev/null 2>&1 ",
                "|| command -v exim4 >/dev/null 2>&1 || command -v exim >/dev/null 2>&1 ",
                "|| [ -d /var/vmail ]; then echo yes; else echo no; fi"
            )
            .to_owned(),
            Self::MaranPanelVersion => "grep -m 1 '^VERSION=' /etc/maran/release.state".to_owned(),
            Self::ListPasswd => "getent passwd".to_owned(),
            Self::ListNginxVhostFiles => concat!(
                "find /etc/nginx/conf.d /etc/nginx/sites-enabled ",
                "-maxdepth 1 -type f -o -type l"
            )
            .to_owned(),
            Self::ReadNginxVhostFile { path } | Self::ReadApacheVhostFile { path } => {
                format!("cat -- '{}'", path.as_str())
            }
            Self::ListApacheVhostFiles => concat!(
                "find /etc/apache2/sites-enabled /etc/httpd/conf.d ",
                "-maxdepth 1 -type f -o -type l"
            )
            .to_owned(),
            Self::ListDatabases => concat!(
                "client=mysql; command -v mysql >/dev/null 2>&1 || client=mariadb; ",
                "\"$client\" --batch --skip-column-names -e ",
                "\"SELECT s.schema_name, COALESCE(SUM(t.data_length + t.index_length), 0) ",
                "FROM information_schema.schemata s ",
                "LEFT JOIN information_schema.tables t ON t.table_schema = s.schema_name ",
                "WHERE s.schema_name NOT IN ('information_schema', 'mysql', ",
                "'performance_schema', 'sys') GROUP BY s.schema_name\""
            )
            .to_owned(),
            Self::ReadCrontab { user } => format!("crontab -u '{}' -l", user.as_str()),
            Self::ListPhpVersions => {
                "find /etc/php /etc/opt/remi -mindepth 1 -maxdepth 1 -type d".to_owned()
            }
            Self::HomeDiskUsage { home } => format!("du -skx -- '{}'", home.as_str()),
            Self::CountMailboxes { home } => format!(
                "find '{0}/Maildir' '{0}/mail' -maxdepth 3 -type d -name cur | wc -l",
                home.as_str()
            ),
            Self::DumpDatabase { database } => format!(
                concat!(
                    "client=mysqldump; command -v mysqldump >/dev/null 2>&1 || client=mariadb-dump; ",
                    "\"$client\" --single-transaction --quick --skip-lock-tables --skip-triggers ",
                    "--hex-blob --default-character-set=utf8mb4 -- '{}'"
                ),
                database.as_str()
            ),
        };

        format!("{PRELUDE}{{ {body}; }} 2>/dev/null")
    }
}

#[cfg(test)]
#[path = "../tests/transfer/remote_command_tests.rs"]
mod tests;
