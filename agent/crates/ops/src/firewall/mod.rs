//! The host firewall: an nftables policy the agent renders, and the runtime
//! bans it drops traffic with.
//!
//! Four things shape everything in this area.
//!
//! **The rendered file IS the rule store.** There is no rule database in the
//! agent. `AgentPaths::nftables_ruleset_path()` holds the complete policy as
//! this agent rendered it, every change re-renders the whole file, and
//! [`RulesetState`] is the parser that reads it back. Parse and render are
//! inverses, so an allow followed by its deny converges on the byte-identical
//! file the host started with — and a file this agent did not write is
//! refused rather than replaced by a policy inferred from it.
//!
//! **`nft -f` is ADDITIVE, and the replace idiom is what defeats that.**
//! Measured on nftables v1.0.9: re-applying a re-rendered ruleset with the
//! 3306 rule removed leaves `3306 rules: 0, loopback rules: 1` when the file
//! carries the create-delete-redeclare idiom, and `3306 rules: 1, loopback
//! rules: 2` when it does not — the removed rule stays LIVE and every other
//! rule is duplicated. The first design of this area passed every fake-host
//! test while leaving the denied port open. The idiom lives in the template;
//! this area refuses to read back a ruleset without it, and the polygon suite
//! asserts on a real kernel that a denied port is really gone.
//!
//! **Bans live in a second table, and its file is applied once.** The rules
//! table is deleted and redeclared on every apply, so a ban kept in it would
//! be erased by every rule change. `table inet maran_bans` therefore holds
//! them, hooked at priority -5 so it runs first, with its own loopback
//! exemption ahead of its drops — nothing, not even a ban, may sever the
//! panel's own web-server-to-application hop. Its file carries the same
//! idiom, so **re-applying it erases every ban in force** (verified), which is
//! why [`ensure_bans_table`] does nothing at all when the table is already
//! loaded.
//!
//! **Every mutation runs under one process-wide lock.** Two of the operations
//! here are check-then-act against state that lives in the kernel or in a
//! file, and a root daemon has exactly one instance per host, so the lock is
//! both necessary and sufficient. Without it, two concurrent first-bans both
//! find the bans table absent and the second's apply erases the first's ban.
//! Its one holder is `firewall_lock`.
//!
//! # How this area MUST be called
//!
//! **Every function here is synchronous, and every one of them MUST be
//! invoked from `tokio::task::spawn_blocking` — never awaited on a runtime
//! worker.** That is a requirement on the caller, not a description of what
//! callers happen to do today. It is the same rule every other `ops` area
//! follows, and the service layer already has the helper for it: the private
//! `run` in each `services/<area>/<area>_service.rs`, which wraps one
//! operation in `spawn_blocking` and maps its error. A firewall handler copies
//! that helper; it does not call an operation directly.
//!
//! The requirement is enforced at runtime rather than by the type system, and
//! only on the mutations. Each of them takes the module lock with
//! `tokio::sync::Mutex::blocking_lock`, which PANICS when it is called from
//! inside an asynchronous context — tokio refusing to let a runtime worker be
//! blocked. So a handler that awaits `allow_port`, `deny_port`,
//! `ban_address`, `unban_address` or `ensure_bans_table` fails on its first
//! request, loudly, with a message naming the cause. `list_rules` and
//! `list_bans` take no lock and will NOT announce the mistake: they will
//! quietly spawn `nft` and read a file on a worker thread, stalling every
//! other in-flight command that shares it (rules/rust.md "Async and
//! blocking"). Both halves of that are reasons to spawn, not reasons to
//! decide per operation.
//!
//! What covers the two readers — and the four mutations, before their first
//! request ever runs — is a check of the CALL SITES rather than a runtime
//! guess: `tests/services/firewall/firewall_service_tests.rs` in the `agent`
//! crate asserts that every call into these six operations sits inside the
//! shared `run_blocking` wrapper, and drives both readers over the rpc on a
//! real runtime. It is
//! written that way because tokio exposes nothing that tells a blocking-pool
//! thread from a worker; `firewall_lock` records what was measured, and the
//! defect the missing measurement produced.
//!
//! The area's shape is the one every area here has: one injectable host trait
//! ([`FirewallHost`]), one file that really touches the machine
//! ([`ProcessFirewallHost`]), one error enum ([`FirewallError`]), and
//! `model/` for the rule, the two host ports, the rule store and a live ban.

mod allow_port;
mod apply_ruleset;
mod ban_address;
mod deny_port;
mod ensure_bans_table;
#[cfg(test)]
#[path = "../tests/firewall/fake_firewall_host.rs"]
pub(crate) mod fake_firewall_host;
mod firewall_error;
mod firewall_host;
mod firewall_lock;
mod list_bans;
mod list_rules;
pub mod model;
mod process_firewall_host;
mod unban_address;

pub use allow_port::allow_port;
pub use ban_address::ban_address;
pub use deny_port::deny_port;
pub use ensure_bans_table::ensure_bans_table;
pub use firewall_error::FirewallError;
pub use firewall_host::FirewallHost;
pub use list_bans::list_bans;
pub use list_rules::list_rules;
pub use model::active_ban::ActiveBan;
pub use model::firewall_rule::FirewallRule;
// Re-exported rather than left for a caller to reach into `maran-templates`
// for. `FirewallRule` carries its protocol as this enum, so a service that
// builds one has to be able to name it — and the agent crate translates, it
// does not render. Without this line that crate would take a dependency on the
// template crate to spell one type (rules/rust.md: `agent` translates,
// `templates` renders).
pub use maran_templates::nftables::nftables_protocol::NftablesProtocol;
pub use model::ruleset_ports::RulesetPorts;
pub use model::ruleset_state::RulesetState;
pub use process_firewall_host::ProcessFirewallHost;
pub use unban_address::unban_address;
