//! What a pending-updates reading looks like on the wire.

use maran_distro::{PendingUpdate, UpdateClassification};

use crate::proto::{
    AgentError, ListPendingUpdatesOk, PendingUpdate as WirePendingUpdate,
    UpdateClassification as WireClassification, list_pending_updates_response,
};

/// Maps one `ListPendingUpdates` outcome onto the response's `oneof`.
///
/// A reading the agent could not take travels as the error arm, never as an
/// empty list: "could not find out" and "nothing to update" are different
/// answers, and the panel draws a badge for the second only. An update that
/// carries no installed version (a family that does not print one) sends the
/// empty string, which the wire documents as "not reported" and the panel's
/// client reads back as absent. An update whose classification was not read
/// sends `UNSPECIFIED` and is never given `ORDINARY`, which is a measured
/// answer (issue #163).
#[must_use]
pub fn reported_pending_updates(
    outcome: Result<Vec<PendingUpdate>, AgentError>,
) -> list_pending_updates_response::Result {
    match outcome {
        Ok(updates) => list_pending_updates_response::Result::Ok(ListPendingUpdatesOk {
            updates: updates.into_iter().map(wire_update).collect(),
        }),
        Err(error) => list_pending_updates_response::Result::Error(error),
    }
}

/// One update on its wire shape.
fn wire_update(update: PendingUpdate) -> WirePendingUpdate {
    WirePendingUpdate {
        package_name: update.package_name,
        available_version: update.available_version,
        installed_version: update.installed_version.unwrap_or_default(),
        source: update.source,
        classification: wire_classification(update.classification) as i32,
    }
}

/// One classification on its wire shape; `Unmeasured` is the wire's zero.
fn wire_classification(classification: UpdateClassification) -> WireClassification {
    match classification {
        UpdateClassification::Unmeasured => WireClassification::Unspecified,
        UpdateClassification::Security => WireClassification::Security,
        UpdateClassification::Ordinary => WireClassification::Ordinary,
    }
}

#[cfg(test)]
#[path = "../../tests/services/monitor/reported_pending_updates_tests.rs"]
mod tests;
