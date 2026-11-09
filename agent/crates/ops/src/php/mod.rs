//! Multi-PHP: which versions this host has, installing one through the
//! installer's root install unit, and the one php-fpm pool each account gets
//! per version.
//!
//! The area has no proto service of its own — it is driven by `sites` and by
//! `accounts` — but it is a full area all the same: one injectable host trait,
//! one file that spawns processes, one error enum, and `model/` for its typed
//! inputs.
//!
//! Two rules shape everything here. The supported versions are a CLOSED set
//! (spec §11), so a version outside 7.4–8.4 is refused by the agent rather
//! than handed to the install unit: what the agent installs is not the
//! caller's choice. And the customer's php.ini settings are a whitelist with
//! bounds, re-validated here rather than trusted from the panel
//! (rules/security.md item 1) — a name that is not on the list is refused, not
//! dropped, and a value carrying a newline is refused, because `pool.conf` is
//! line-oriented exactly as an nginx vhost is.

mod apply_php_pool_settings;
mod classify_install_failure;
#[cfg(test)]
#[path = "../tests/php/fake_php_host.rs"]
pub(crate) mod fake_php_host;
mod install_php_version;
mod list_installable_php_versions;
mod list_php_extensions;
mod list_php_versions;
pub mod model;
mod php_host;
mod php_op_error;
mod process_php_host;
mod reconcile_opcache_drop_ins;
mod reload_pool_trees;
mod remove_account_pools;
mod remove_pool;
pub(crate) mod render_pool;
mod run_install_unit;
mod set_php_extension;
mod supported_versions;
pub(crate) mod write_opcache_drop_in;
pub(crate) mod write_pool;

pub use apply_php_pool_settings::apply_php_pool_settings;
pub use install_php_version::install_php_version;
pub use list_installable_php_versions::list_installable_php_versions;
pub use list_php_extensions::list_php_extensions;
pub use list_php_versions::list_php_versions;
pub use model::extension_availability::ExtensionAvailability;
pub use model::installable_php_version::InstallablePhpVersion;
pub use model::installed_php_version::InstalledPhpVersion;
pub use model::opcache_drop_in_outcome::OpcacheDropInOutcome;
pub use model::override_kind::OverrideKind;
pub use model::php_extension::PhpExtension;
pub use model::php_override::PhpOverride;
pub use model::pool_input::PoolInput;
pub use model::pool_paths::PoolPaths;
pub use model::pool_tree_decision::PoolTreeDecision;
pub use model::pool_tree_outcome::PoolTreeOutcome;
pub use php_host::PhpHost;
pub use php_op_error::PhpOpError;
pub use process_php_host::ProcessPhpHost;
pub use reconcile_opcache_drop_ins::reconcile_opcache_drop_ins;
pub use reload_pool_trees::reload_pool_trees;
pub use remove_account_pools::remove_account_pools;
pub use remove_pool::remove_pool;
pub use set_php_extension::set_php_extension;
pub use write_opcache_drop_in::write_opcache_drop_in;
pub use write_pool::write_pool;

pub(crate) use list_php_versions::is_installed;
