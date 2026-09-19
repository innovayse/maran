//! The one mapping from a grant-repair outcome onto the wire.

use maran_ops::db::{GrantRepairRefusal, GrantRepairReport, RefusedGrant, RepairedGrant};

use crate::proto::{
    GrantRepairRefusal as WireRefusal, RefusedGrant as WireRefusedGrant, RepairDatabaseGrantsOk,
    RepairedGrant as WireRepairedGrant,
};

/// Converts the report `ops::db::repair_grants` returns into the response body.
///
/// It is a named file rather than a closure inside the handler for the reason
/// rules/rust.md gives: it is a decision with more than one case — four refusal
/// reasons, each of which an operator acts on differently — and a mapping inlined
/// in a service method can be deleted without a single test going red.
///
/// The counts are narrowed to `u32` by saturation rather than by a cast that
/// wraps. A host with more than four billion grant rows cannot exist, and if one
/// did, a wrapped count would report a repaired host as untouched.
pub(crate) fn wire_grant_repair_report(report: GrantRepairReport) -> RepairDatabaseGrantsOk {
    RepairDatabaseGrantsOk {
        examined_grants: u32::try_from(report.examined).unwrap_or(u32::MAX),
        already_correct: u32::try_from(report.already_correct).unwrap_or(u32::MAX),
        repaired: report.repaired.into_iter().map(wire_repaired).collect(),
        would_repair: report.would_repair.into_iter().map(wire_repaired).collect(),
        refused: report.refused.into_iter().map(wire_refused).collect(),
    }
}

/// Converts one rewritten grant onto the wire.
fn wire_repaired(grant: RepairedGrant) -> WireRepairedGrant {
    WireRepairedGrant {
        database_name: grant.database.as_str().to_owned(),
        db_username: grant.user.as_str().to_owned(),
        also_matched_databases: grant.also_matched,
    }
}

/// Converts one refused row onto the wire, reason included.
fn wire_refused(grant: RefusedGrant) -> WireRefusedGrant {
    WireRefusedGrant {
        grant_host: grant.host,
        database_name: grant.database,
        db_username: grant.user,
        reason: wire_reason(grant.reason) as i32,
    }
}

/// Maps one refusal reason onto its contract value.
///
/// `GrantRepairRefusal` is `#[non_exhaustive]`, so a reason added in the ops crate
/// lands on `Unspecified` here rather than failing this build — and an operator
/// then sees a refusal with no reason instead of a row silently vanishing from
/// the report, which is the failure mode worth avoiding.
fn wire_reason(reason: GrantRepairRefusal) -> WireRefusal {
    match reason {
        GrantRepairRefusal::HostIsNotLocalhost => WireRefusal::HostIsNotLocalhost,
        GrantRepairRefusal::NotThePanelsNaming => WireRefusal::NotThePanelsNaming,
        GrantRepairRefusal::UnrecognisedPrivileges => WireRefusal::UnrecognisedPrivileges,
        GrantRepairRefusal::PartiallyOrUnfamiliarlyEscaped => {
            WireRefusal::PartiallyOrUnfamiliarlyEscaped
        }
        _ => WireRefusal::Unspecified,
    }
}

#[cfg(test)]
#[path = "../../tests/services/db/wire_grant_repair_report_tests.rs"]
mod tests;
