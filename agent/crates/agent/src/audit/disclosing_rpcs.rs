//! The closed list of agent rpcs that yield an audit line for what they disclose.

/// Every agent rpc that changes nothing on the host but hands a customer's own
/// data back to the panel, as the full gRPC path the request carries
/// (`/maran.agent.v1.<Service>/<Method>`).
///
/// A third class beside the mutating and the read-only one, and not a member of
/// either, because it is neither: `ReadFile` changes nothing, so it does not
/// belong with the writers, and it streams the contents of a hosting customer's
/// file out of the only root process, so it does not belong with the rpcs that
/// report names and sizes. If `maran-api` were compromised, the independent
/// record that the agent was asked to read an account's file at time T is the
/// one thing the panel's own database cannot be trusted to hold.
///
/// The line is the same line a mutating rpc writes (account, outcome, duration)
/// and, like it, carries no path: the trail says that a read happened and
/// whether it was served, not which file. `ListDirectory` and `SearchEntries`
/// stay read-only on purpose: they disclose names, and a file manager polls them.
///
/// Closed and hand-written like the other two; the census in `src/tests/audit/`
/// fails a path of the contract that is in none of the three lists, or in two.
/// Sorted in byte order and free of duplicates.
pub const DISCLOSING_RPCS: &[&str] = &["/maran.agent.v1.FilesService/ReadFile"];

#[cfg(test)]
#[path = "../tests/audit/disclosing_rpcs_tests.rs"]
mod tests;
