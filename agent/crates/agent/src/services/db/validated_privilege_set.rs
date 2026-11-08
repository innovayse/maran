//! Turning the wire's privilege numbers into the closed set the agent grants.

use maran_agent_core::validation::db::database_privilege::DatabasePrivilege;
use maran_agent_core::validation::db::database_privilege_set::DatabasePrivilegeSet;

use crate::proto::{AgentError, DatabasePrivilege as WirePrivilege};
use crate::services::wire::invalid_input::invalid_input;

/// The core privilege a defined, non-zero wire value stands for.
///
/// One `match`, so the numbers live in the contract alone and the privilege type
/// carries none. `Unspecified` has no arm that produces a privilege: it is refused
/// by the caller, never defaulted to one.
const fn privilege_of(wire: WirePrivilege) -> Option<DatabasePrivilege> {
    match wire {
        WirePrivilege::Unspecified => None,
        WirePrivilege::Select => Some(DatabasePrivilege::Select),
        WirePrivilege::Insert => Some(DatabasePrivilege::Insert),
        WirePrivilege::Update => Some(DatabasePrivilege::Update),
        WirePrivilege::Delete => Some(DatabasePrivilege::Delete),
        WirePrivilege::Create => Some(DatabasePrivilege::Create),
        WirePrivilege::Drop => Some(DatabasePrivilege::Drop),
        WirePrivilege::Alter => Some(DatabasePrivilege::Alter),
        WirePrivilege::Index => Some(DatabasePrivilege::Index),
        WirePrivilege::References => Some(DatabasePrivilege::References),
        WirePrivilege::CreateTemporaryTables => Some(DatabasePrivilege::CreateTemporaryTables),
        WirePrivilege::LockTables => Some(DatabasePrivilege::LockTables),
        WirePrivilege::CreateView => Some(DatabasePrivilege::CreateView),
        WirePrivilege::ShowView => Some(DatabasePrivilege::ShowView),
        WirePrivilege::CreateRoutine => Some(DatabasePrivilege::CreateRoutine),
        WirePrivilege::AlterRoutine => Some(DatabasePrivilege::AlterRoutine),
        WirePrivilege::Execute => Some(DatabasePrivilege::Execute),
        WirePrivilege::Trigger => Some(DatabasePrivilege::Trigger),
        WirePrivilege::Event => Some(DatabasePrivilege::Event),
    }
}

/// Builds the set a `SetDatabaseUserPrivileges` request asks the login to hold.
///
/// The length is bounded first, then each number is read: an unspecified value,
/// a number the contract does not define, a repeat and an over-long list are all
/// refused, and nothing is ignored or defaulted. An empty list is valid and means
/// "hold nothing". The closed enum is what keeps `GRANT OPTION` and every global
/// privilege unrepresentable; this function is the only place wire numbers become
/// privileges.
///
/// # Errors
///
/// Returns the wire error for more entries than the closed set holds, for a number
/// that is zero or undefined, and for a privilege named twice.
pub fn validated_privilege_set(privileges: &[i32]) -> Result<DatabasePrivilegeSet, AgentError> {
    if privileges.len() > DatabasePrivilege::ALL.len() {
        return Err(invalid_input(format!(
            "{} privileges given, more than the {} that exist",
            privileges.len(),
            DatabasePrivilege::ALL.len()
        )));
    }

    let mut resolved = Vec::with_capacity(privileges.len());
    for number in privileges {
        let privilege = WirePrivilege::try_from(*number)
            .ok()
            .and_then(privilege_of)
            .ok_or_else(|| invalid_input(String::from("a privilege is unspecified or unknown")))?;
        resolved.push(privilege);
    }

    DatabasePrivilegeSet::of(&resolved).map_err(|error| invalid_input(error.to_string()))
}

#[cfg(test)]
#[path = "../../tests/services/db/validated_privilege_set_tests.rs"]
mod tests;
