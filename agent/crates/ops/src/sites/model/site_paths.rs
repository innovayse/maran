//! Every filesystem location one site occupies, derived from its identity.

use std::path::PathBuf;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;

/// Directory, inside the account's home, holding one directory per site.
const SITES_DIRECTORY: &str = "sites";

/// The paths a single site occupies, derived once from its account and domain.
///
/// Derived rather than stored: a site's locations are a function of its
/// identity, and a stored copy is one that can disagree with the site it
/// belongs to. Every operation in this area asks for them the same way, so
/// `create_site` writes exactly the file `delete_site` removes and
/// `disable_site` replaces.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SitePaths {
    /// `/home/<account>/sites/<domain>` — the document root (spec §11).
    pub document_root: PathBuf,
    /// `/var/log/maran/sites/<account>` — where the two log files below live.
    ///
    /// **Outside the account's home, and that is the whole security shape of
    /// this type.** The web server's MASTER process runs as root and is what
    /// opens these two files, without `O_NOFOLLOW`; while this directory was
    /// `/home/<account>/logs` the customer owned it and could replace either
    /// file with a symbolic link to anything root can write to, which is a root
    /// compromise on the next reload. See
    /// [`AgentPaths::SITE_LOG_ROOT`] for the full argument and
    /// `docs/superpowers/notes/2026-09-09-site-logs-threat-note.md` for the
    /// probe that proved it.
    pub log_directory: PathBuf,
    /// The site's access log, written by the web server as root.
    pub access_log: PathBuf,
    /// The site's error log, written by the web server as root.
    pub error_log: PathBuf,
    /// The vhost in the agent's own include directory — a file the agent
    /// owns outright and the distribution's packaging never touches (spec §9).
    pub config_path: PathBuf,
}

impl SitePaths {
    /// Derives every path for `domain` under `account`.
    ///
    /// Both arguments are validated types, which is what makes the joins below
    /// safe to perform as strings: a `Domain` cannot contain `/`, `..` or a
    /// NUL, so no component can escape the directory it is joined into. The
    /// document root is still re-checked with `resolve_in_home` once it exists
    /// — this function names a path, it does not prove one is contained.
    ///
    /// The two logs are NOT under the home and are not re-checked against it:
    /// their containment comes from an ancestor chain no unprivileged uid can
    /// write to or traverse, which is a stronger guarantee than any check made
    /// on a directory the customer owns could be.
    #[must_use]
    pub fn for_site(account: &AccountName, domain: &Domain) -> Self {
        let home = PathBuf::from(AgentPaths::ACCOUNT_HOME_ROOT).join(account.as_str());
        let log_directory = AgentPaths::account_site_log_dir(account);

        Self {
            document_root: home.join(SITES_DIRECTORY).join(domain.as_str()),
            access_log: log_directory.join(format!("{}.access.log", domain.as_str())),
            error_log: log_directory.join(format!("{}.error.log", domain.as_str())),
            log_directory,
            config_path: PathBuf::from(AgentPaths::NGINX_INCLUDE_DIRECTORY)
                .join(format!("{}.conf", domain.as_str())),
        }
    }

    /// The path of this site's document root relative to the account's home,
    /// as `resolve_in_home` expects it.
    #[must_use]
    pub fn document_root_in_home(domain: &Domain) -> PathBuf {
        PathBuf::from(SITES_DIRECTORY).join(domain.as_str())
    }

    /// The directory holding `account`'s site logs.
    ///
    /// The directory and not a log file: a site that has served no request yet
    /// has no access log, and a tail must be able to tell that apart from a
    /// path it must refuse. The tail opens THIS once, proves it is a root-owned
    /// directory, and reaches each log through that descriptor with `openat` —
    /// so no rename of an intermediate component can redirect a running stream.
    ///
    /// There is no `resolve_in_home` counterpart any more, and there should not
    /// be: the path is outside every home, so asking whether it is contained in
    /// one would be answering a question nobody is asking.
    #[must_use]
    pub fn log_directory_for(account: &AccountName) -> PathBuf {
        AgentPaths::account_site_log_dir(account)
    }
}
