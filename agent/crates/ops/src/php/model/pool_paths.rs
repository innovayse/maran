//! Every location one php-fpm pool occupies, derived from its identity.

use std::path::PathBuf;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::name::AccountName;
use maran_agent_core::validation::php_version::PhpVersion;
use maran_distro::DistroAdapter;

/// The directory, inside an account's home, holding everything the panel puts
/// there on the account's behalf.
///
/// Dot-prefixed so it does not appear in a file listing beside the customer's
/// own `sites/` and `logs/`, and named once here so a later addition lands
/// beside these two rather than loose in the home.
const MARAN_DIRECTORY: &str = ".maran";

/// Subdirectory of the above holding PHP session files.
const SESSIONS_DIRECTORY: &str = "sessions";

/// Subdirectory of the above holding in-flight uploads.
const UPLOADS_DIRECTORY: &str = "tmp";

/// The paths and names one pool occupies, derived once from the account and
/// the version it belongs to.
///
/// Derived rather than stored, for the reason `sites::SitePaths` is: a pool's
/// locations are a function of its identity, and a stored copy is one that can
/// disagree with the pool it describes.
///
/// [`Self::socket_path`] is the field to be careful with. `sites::render_vhost`
/// writes `fastcgi_pass unix:<PHP_FPM_SOCKET_DIRECTORY>/<account>-<version>.sock`
/// into every PHP vhost, and this type produces the `listen` at the other end
/// of that socket. The two are built from the same constant and the same
/// `{account}-{version}` shape on purpose: if they ever disagreed nothing
/// would fail to start — `nginx -t` passes, `php-fpm -t` passes, the pool
/// listens and the vhost connects — and every PHP request on the host would
/// return 502 with no configuration error anywhere to explain it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PoolPaths {
    /// The pool's section name, `[acme-8.3]` in the rendered file.
    ///
    /// Unique per account × version, which is what allows several versions'
    /// php-fpm masters to be running with a pool for the same account in each.
    pub pool_name: String,
    /// The pool file, in the version's own pool directory from the adapter.
    pub config_path: PathBuf,
    /// The unix socket the pool listens on and the vhost connects to.
    pub socket_path: PathBuf,
    /// The directory that socket lives in, which must exist before php-fpm
    /// can bind in it.
    pub socket_directory: PathBuf,
    /// The account's home, the root of what the pool's `open_basedir` grants.
    pub home_directory: PathBuf,
    /// Where PHP writes this account's session files.
    ///
    /// Inside the home and owned by the account, never the shared `/tmp` PHP
    /// falls back to when the packaged session directory is root-owned — that
    /// fallback puts one customer's `sess_*` files where every other
    /// customer's PHP can enumerate and read them.
    pub session_directory: PathBuf,
    /// Where PHP writes this account's in-flight uploads, for the same reason.
    pub upload_temporary_directory: PathBuf,
}

impl PoolPaths {
    /// Derives every path for `account`'s pool at `version`.
    ///
    /// Both arguments are validated types, which is what makes the joins below
    /// safe to perform as strings: an [`AccountName`] cannot contain `/` or
    /// `..` and a [`PhpVersion`] is two groups of digits, so no component can
    /// escape the directory it is joined into and no component can end a line
    /// in the file it is written to.
    #[must_use]
    pub fn for_pool(
        distro: &dyn DistroAdapter,
        account: &AccountName,
        version: &PhpVersion,
    ) -> Self {
        let pool_name = format!("{}-{}", account.as_str(), version.as_str());
        let socket_directory = PathBuf::from(AgentPaths::PHP_FPM_SOCKET_DIRECTORY);
        let home_directory = PathBuf::from(AgentPaths::ACCOUNT_HOME_ROOT).join(account.as_str());

        Self {
            config_path: PathBuf::from(distro.php_fpm_pool_directory(version.as_str()))
                .join(format!("{}.conf", account.as_str())),
            socket_path: socket_directory.join(format!("{pool_name}.sock")),
            socket_directory,
            session_directory: home_directory
                .join(MARAN_DIRECTORY)
                .join(SESSIONS_DIRECTORY),
            upload_temporary_directory: home_directory
                .join(MARAN_DIRECTORY)
                .join(UPLOADS_DIRECTORY),
            home_directory,
            pool_name,
        }
    }
}
