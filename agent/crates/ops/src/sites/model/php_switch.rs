//! Everything a PHP version switch needs beyond the site it is switching.

use maran_agent_core::validation::php_version::PhpVersion;

use crate::php::model::php_override::PhpOverride;

/// The four panel-owned facts a version switch carries, gathered into one
/// value.
///
/// A struct rather than four more parameters, and not only because the argument
/// list grew past what clippy will accept. Three of these are `u32`, `bool` and
/// a slice, which at a call site are three unlabelled values whose order
/// nothing checks — and one of them decides whether a php-fpm pool is
/// DESTROYED. Naming them at the call site is worth a type.
///
/// Every field is the panel's to supply: none of them can be read back off the
/// host (rules/architecture.md — truth lives in PostgreSQL, and what is on disk
/// is a rendering of it).
pub struct PhpSwitch<'a> {
    /// The installed version the site is moving to.
    pub version: &'a PhpVersion,

    /// The account plan's worker budget, which becomes the new pool's
    /// `pm.max_children`.
    pub max_children: u32,

    /// The customer's whitelisted php.ini settings, re-applied to the new pool
    /// because it is written from scratch and nothing carries the old one's
    /// contents forward.
    pub overrides: &'a [PhpOverride],

    /// Whether the version being LEFT may have its pool removed.
    ///
    /// True only when the panel has established that no other site of this
    /// account still uses it: a pool is shared per account × version, so
    /// removing it because this one site moved would take the account's other
    /// sites off the air. False leaves the old pool standing, which is safe and
    /// merely wasteful.
    pub remove_previous_pool: bool,
}
