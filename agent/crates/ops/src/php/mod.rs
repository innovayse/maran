//! Multi-PHP: which versions this host has, installing one from the family's
//! repository, and the one php-fpm pool each account gets per version.
//!
//! The area has no proto service of its own — it is driven by `sites` and by
//! `accounts` — but it is a full area all the same: one injectable host trait,
//! one file that spawns processes, one error enum, and `model/` for its typed
//! inputs.
//!
//! Two rules shape everything here. The supported versions are a CLOSED set
//! (spec §11), so a version outside 7.4–8.4 is refused by the agent rather
//! than handed to a package manager: what the agent installs is not the
//! caller's choice. And the customer's php.ini settings are a whitelist with
//! bounds, re-validated here rather than trusted from the panel
//! (rules/security.md item 1) — a name that is not on the list is refused, not
//! dropped, and a value carrying a newline is refused, because `pool.conf` is
//! line-oriented exactly as an nginx vhost is.

#[cfg(test)]
#[path = "../tests/php/fake_php_host.rs"]
pub(crate) mod fake_php_host;
mod install_php_version;
mod list_php_versions;
pub mod model;
mod php_host;
mod php_op_error;
mod process_php_host;
mod remove_account_pools;
mod remove_pool;
mod supported_versions;
pub(crate) mod write_pool;

pub use install_php_version::install_php_version;
pub use list_php_versions::list_php_versions;
pub use model::installed_php_version::InstalledPhpVersion;
pub use model::override_kind::OverrideKind;
pub use model::php_override::PhpOverride;
pub use model::pool_input::PoolInput;
pub use model::pool_paths::PoolPaths;
pub use php_host::PhpHost;
pub use php_op_error::PhpOpError;
pub use process_php_host::ProcessPhpHost;
pub use remove_account_pools::remove_account_pools;
pub use remove_pool::remove_pool;
pub use write_pool::write_pool;

pub(crate) use list_php_versions::is_installed;
