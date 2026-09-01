//! Everything the pool writer needs about the pool it writes.

use maran_agent_core::validation::name::AccountName;
use maran_agent_core::validation::php_version::PhpVersion;

use crate::php::model::php_override::PhpOverride;

/// Everything `write_pool` needs, already validated.
///
/// Every field is a type that cannot hold an invalid value: an
/// [`AccountName`], a [`PhpVersion`] and a list of [`PhpOverride`]s that only
/// [`PhpOverride::parse`] can produce. There is no `&str` here at all, which
/// is what lets the pool template escape nothing — the values reaching it have
/// each been through a parser (rules/rust.md "Validation first").
///
/// `max_children` is materialised from the account's plan by the panel (spec
/// §8) and is a `u32` rather than a parsed type because a number has no shape
/// to get wrong: every `u32` renders as digits, and no digit ends a line.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PoolInput {
    /// The account whose uid the pool's workers run as.
    pub account: AccountName,
    /// The PHP version this pool belongs to. One pool per account × version
    /// (spec §11), so this is half the pool's identity, not a setting on it.
    pub version: PhpVersion,
    /// Maximum worker processes, from the account's plan worker limit.
    pub max_children: u32,
    /// The whitelisted php.ini settings the customer has set.
    pub overrides: Vec<PhpOverride>,
}
