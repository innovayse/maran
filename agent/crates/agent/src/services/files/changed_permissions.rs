//! Turning the outcome of a permission change into the wire's response.

use maran_ops::files::model::permission_report::PermissionReport;

use crate::proto::{
    AgentError, ChangePermissionsOk, ChangePermissionsResponse,
    change_permissions_response::Result as Outcome,
};

/// Builds the `ChangePermissionsResponse` for a finished change: how many entries were changed
/// and how many skipped, or the typed error.
///
/// Failures travel in the payload, not as a gRPC status (rules/proto.md).
#[must_use]
pub fn changed_permissions(
    result: Result<PermissionReport, AgentError>,
) -> ChangePermissionsResponse {
    let result = match result {
        Ok(report) => Outcome::Ok(ChangePermissionsOk {
            changed: report.changed,
            skipped: report.skipped,
        }),
        Err(error) => Outcome::Error(error),
    };

    ChangePermissionsResponse {
        result: Some(result),
    }
}

#[cfg(test)]
#[path = "../../tests/services/files/changed_permissions_tests.rs"]
mod tests;
