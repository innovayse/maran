//! The one mapping from a home-group repair outcome onto the wire.

use maran_ops::accounts::{
    HomeGroupRepairRefusal, HomeGroupRepairReport, RefusedHome, RepairedHome,
};

use crate::proto::{
    HomeGroupRepairRefusal as WireRefusal, RefusedHome as WireRefusedHome,
    RepairAccountHomeGroupsOk, RepairedHome as WireRepairedHome,
};

/// Converts the report `ops::accounts::repair_home_groups` returns into the
/// response body.
///
/// A named file rather than a closure inside the service method, for the same
/// reason `wire_grant_repair_report` in `services::db` is one: this is a
/// decision with more than one case — six refusal reasons, each of which an
/// operator acts on differently — and a mapping inlined in a service method
/// can be deleted without a single test going red.
///
/// The counts are narrowed to `u32` by saturation rather than by a cast that
/// wraps. A host with more than four billion hosting accounts cannot exist,
/// and if one did, a wrapped count would report a repaired host as untouched.
#[must_use]
pub(crate) fn wire_home_group_repair_report(
    report: HomeGroupRepairReport,
) -> RepairAccountHomeGroupsOk {
    RepairAccountHomeGroupsOk {
        examined: u32::try_from(report.examined).unwrap_or(u32::MAX),
        already_correct: u32::try_from(report.already_correct).unwrap_or(u32::MAX),
        repaired: report.repaired.into_iter().map(wire_repaired).collect(),
        would_repair: report.would_repair.into_iter().map(wire_repaired).collect(),
        refused: report.refused.into_iter().map(wire_refused).collect(),
    }
}

/// Converts one re-grouped — or would-be-re-grouped — home onto the wire.
fn wire_repaired(home: RepairedHome) -> WireRepairedHome {
    WireRepairedHome {
        account_username: home.account.as_str().to_owned(),
        home: home.home,
    }
}

/// Converts one refused account onto the wire, reason included.
fn wire_refused(home: RefusedHome) -> WireRefusedHome {
    WireRefusedHome {
        account_username: home.account.as_str().to_owned(),
        home: home.home,
        reason: wire_reason(home.reason) as i32,
    }
}

/// Maps one refusal reason onto its contract value.
///
/// `HomeGroupRepairRefusal` is `#[non_exhaustive]`, so a reason added in the
/// ops crate lands on `Unspecified` here rather than failing this build — and
/// an operator then sees a refusal with no reason instead of an account
/// silently vanishing from the report, which is the failure mode worth
/// avoiding.
fn wire_reason(reason: HomeGroupRepairRefusal) -> WireRefusal {
    match reason {
        HomeGroupRepairRefusal::HomeNotAtExpectedPath => WireRefusal::HomeNotAtExpectedPath,
        HomeGroupRepairRefusal::HomeMissing => WireRefusal::HomeMissing,
        HomeGroupRepairRefusal::Symlink => WireRefusal::Symlink,
        HomeGroupRepairRefusal::NotADirectory => WireRefusal::NotADirectory,
        HomeGroupRepairRefusal::DifferentMount => WireRefusal::DifferentMount,
        HomeGroupRepairRefusal::OwnerMismatch => WireRefusal::OwnerMismatch,
        _ => WireRefusal::Unspecified,
    }
}

#[cfg(test)]
#[path = "../../tests/services/accounts/wire_home_group_repair_report_tests.rs"]
mod tests;
