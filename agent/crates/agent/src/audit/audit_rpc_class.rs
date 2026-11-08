//! How the audit layer classifies one agent rpc: audited or not.

use super::disclosing_rpcs::DISCLOSING_RPCS;
use super::mutating_rpcs::MUTATING_RPCS;
use super::read_only_rpcs::READ_ONLY_RPCS;

/// How the audit layer treats one agent rpc.
///
/// The class is decided by the request path alone, before any request byte is
/// read, so a hostile peer cannot influence it: the path is compared against
/// three closed lists and never parsed.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AuditRpcClass {
    /// The rpc changes the host or starts such work; it yields one audit line.
    Mutating,
    /// The rpc only reports state; it yields no audit line.
    ReadOnly,
    /// The rpc changes nothing but hands the contents of a customer's file back
    /// to the panel; it yields one audit line, like a mutating rpc.
    Disclosing,
}

impl AuditRpcClass {
    /// Classifies a full gRPC request path such as
    /// `/maran.agent.v1.SitesService/CreateSite`.
    ///
    /// Returns `None` for a path in neither list, including a path of another
    /// service, a malformed one and the empty string. The caller treats `None`
    /// as "not audited" and never prints the path.
    #[must_use]
    pub fn of(path: &str) -> Option<AuditRpcClass> {
        if MUTATING_RPCS.contains(&path) {
            Some(AuditRpcClass::Mutating)
        } else if DISCLOSING_RPCS.contains(&path) {
            Some(AuditRpcClass::Disclosing)
        } else if READ_ONLY_RPCS.contains(&path) {
            Some(AuditRpcClass::ReadOnly)
        } else {
            None
        }
    }
}

#[cfg(test)]
#[path = "../tests/audit/audit_rpc_class_tests.rs"]
mod tests;
