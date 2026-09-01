//! Every filesystem location one site occupies, derived from its identity.

use std::path::PathBuf;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::domain::Domain;
use maran_agent_core::validation::name::AccountName;

/// Directory, inside the account's home, holding one directory per site.
const SITES_DIRECTORY: &str = "sites";

/// Directory, inside the account's home, holding the web server's logs.
const LOGS_DIRECTORY: &str = "logs";

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
    /// `/home/<account>/logs` — where the two log files below live.
    pub log_directory: PathBuf,
    /// The site's access log, written by the web server.
    pub access_log: PathBuf,
    /// The site's error log, written by the web server.
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
    #[must_use]
    pub fn for_site(account: &AccountName, domain: &Domain) -> Self {
        let home = PathBuf::from(AgentPaths::ACCOUNT_HOME_ROOT).join(account.as_str());
        let log_directory = home.join(LOGS_DIRECTORY);

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

    /// The path of the account's log directory relative to its home, as
    /// `resolve_in_home` expects it.
    ///
    /// The directory and not a log file: a site that has served no request yet
    /// has no access log, and a tail must be able to tell that apart from a
    /// path that escaped the home.
    #[must_use]
    pub fn log_directory_in_home() -> PathBuf {
        PathBuf::from(LOGS_DIRECTORY)
    }
}
