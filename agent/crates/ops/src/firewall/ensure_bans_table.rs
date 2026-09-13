//! EnsureBansTable: load the table runtime bans live in, once and only once.

use maran_agent_core::agent_paths::AgentPaths;
use maran_distro::DistroAdapter;
use maran_templates::nftables::nftables_bans_table::NftablesBansTable;

use crate::firewall::apply_ruleset::apply_ruleset;
use crate::firewall::firewall_error::FirewallError;
use crate::firewall::firewall_host::FirewallHost;
use crate::firewall::firewall_lock::firewall_lock;

/// The nftables address family both of this agent's tables live in.
///
/// `inet` rather than `ip` and `ip6`, so one table's rules cover both address
/// families — verified on the polygon. It is not a platform fact: it is this
/// agent's own choice, made in the templates, and named here because the
/// commands that address the table have to spell it the same way.
pub(super) const TABLE_FAMILY: &str = "inet";

/// The table runtime bans live in.
///
/// A second table, separate from the rules table, because the rules table is
/// REPLACED whole on every apply and a ban living in it would be erased by
/// every rule change.
///
/// This name and the two set names below must match the bans template
/// (`templates/nftables/bans_table.nft.j2`), which is what actually declares
/// them. Nothing in the type system ties the two together, so a test renders
/// the template and asserts that all three names appear in it.
pub(super) const BANS_TABLE: &str = "maran_bans";

/// The set of banned IPv4 addresses.
pub(super) const BANNED_V4_SET: &str = "banned_v4";

/// The set of banned IPv6 addresses.
pub(super) const BANNED_V6_SET: &str = "banned_v6";

/// `nft`'s verb for showing an object.
const LIST: &str = "list";

/// `nft`'s noun for a table.
const TABLE: &str = "table";

/// Makes sure `table inet maran_bans` is loaded, and leaves it completely
/// alone when it already is.
///
/// # Doing nothing is the point
///
/// The bans file carries the same create-delete-redeclare idiom the ruleset
/// does, so **re-applying it over an existing table ERASES the elements in
/// its sets** — every ban currently in force, gone, verified on nftables
/// v1.0.9. That is why this is a check-then-apply and not an unconditional
/// apply: an unconditional one would silently release every banned address
/// each time anything asked for a ban.
///
/// The check and the apply are two operations with a gap between them, and
/// the gap is why every mutating operation in this area runs under
/// the module lock: two concurrent first-bans would otherwise both find the
/// table absent, both apply the file, and the second apply would erase the
/// first's ban while the panel recorded both.
///
/// On a healthy host this is a belt: the installer seeds the bans file and
/// loads it at install time, so the table is already there and this returns
/// without touching anything. It earns its place on the hosts where that did
/// not happen — a restored image, a manually flushed ruleset, a service that
/// was stopped and started — where the alternative is a ban that reports
/// success and drops nothing.
///
/// # Calling this
///
/// Synchronous, and it MUST be invoked from `tokio::task::spawn_blocking` —
/// never awaited on a runtime worker. It spawns `nft`, writes to disk, and
/// takes the module lock with `blocking_lock`, which PANICS rather than block
/// a worker. See this area's module documentation for the whole requirement.
///
/// # Errors
///
/// - [`FirewallError::NftFailed`] when `nft` cannot be started, or when
///   loading the bans file fails.
/// - [`FirewallError::RenderFailed`] when the bans template fails to render.
/// - [`FirewallError::RuleRefusedByNft`] when `nft --check` rejects the
///   rendered bans file; nothing is loaded and no existing table is touched.
/// - [`FirewallError::StagingFailed`] when the file cannot be written or
///   renamed into place.
pub fn ensure_bans_table(
    host: &dyn FirewallHost,
    distro: &dyn DistroAdapter,
) -> Result<(), FirewallError> {
    let _guard = firewall_lock();

    ensure_bans_table_under_lock(host, distro)
}

/// The body of [`ensure_bans_table`], for a caller that already holds
/// the module lock.
///
/// It exists because `ban_address` has to hold ONE lock across the ensure and
/// the element it then adds — checking for the table, releasing the lock and
/// taking it again would leave exactly the gap the lock is there to close.
/// The lock is not reentrant, so the shared work is the part below the lock
/// rather than the whole operation.
///
/// # Errors
///
/// The same conditions as [`ensure_bans_table`].
pub(super) fn ensure_bans_table_under_lock(
    host: &dyn FirewallHost,
    distro: &dyn DistroAdapter,
) -> Result<(), FirewallError> {
    let listed = host.run(
        distro.nft_binary(),
        &[LIST, TABLE, TABLE_FAMILY, BANS_TABLE],
    )?;
    if listed.status == 0 {
        // The table is loaded, so its sets hold whatever bans are in force.
        // Applying the file now would take them all away.
        return Ok(());
    }

    let contents = NftablesBansTable {}
        .render_config()
        .map_err(|_| FirewallError::RenderFailed)?;

    apply_ruleset(host, distro, AgentPaths::nftables_bans_path(), &contents)
}

#[cfg(test)]
#[path = "../tests/firewall/ensure_bans_table_tests.rs"]
mod tests;
