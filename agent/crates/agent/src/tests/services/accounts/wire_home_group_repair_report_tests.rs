//! The home-group repair report as the contract carries it.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;
use maran_ops::accounts::{
    HomeGroupRepairRefusal, HomeGroupRepairReport, RefusedHome, RepairedHome,
};

use super::wire_home_group_repair_report;
use crate::proto::HomeGroupRepairRefusal as WireRefusal;

/// The account the repaired home in these tests belongs to.
fn account() -> AccountName {
    AccountName::parse("h_stco").expect("valid")
}

/// A home that was, or would be, re-grouped.
fn repaired() -> RepairedHome {
    RepairedHome {
        account: account(),
        home: "/home/h_stco".to_owned(),
    }
}

/// A repaired home carries its account name and its path onto the wire.
#[test]
fn a_repaired_home_carries_its_account_and_path() {
    let report = HomeGroupRepairReport {
        examined: 2,
        already_correct: 1,
        repaired: vec![repaired()],
        ..HomeGroupRepairReport::default()
    };

    let wire = wire_home_group_repair_report(report);

    assert_eq!(wire.examined, 2);
    assert_eq!(wire.already_correct, 1);
    assert_eq!(wire.repaired[0].account_username, "h_stco");
    assert_eq!(wire.repaired[0].home, "/home/h_stco");
    assert!(wire.would_repair.is_empty());
}

/// A report-only pass fills `would_repair` and leaves `repaired` empty.
#[test]
fn a_report_only_pass_fills_would_repair_and_nothing_else() {
    let report = HomeGroupRepairReport {
        examined: 1,
        would_repair: vec![repaired()],
        ..HomeGroupRepairReport::default()
    };

    let wire = wire_home_group_repair_report(report);

    assert!(wire.repaired.is_empty());
    assert_eq!(wire.would_repair.len(), 1);
}

/// Every refusal reason maps onto its own contract value.
///
/// One case each, because the reasons are what an operator acts on and two of
/// them collapsing onto one value would send them to the wrong row. The raw
/// account name and path travel untouched: a refused row is refused precisely
/// because it is not a home this agent's own `create` produced.
#[test]
fn every_refusal_reason_reaches_the_wire_as_its_own_value() {
    let reasons = [
        (
            HomeGroupRepairRefusal::HomeNotAtExpectedPath,
            WireRefusal::HomeNotAtExpectedPath,
        ),
        (
            HomeGroupRepairRefusal::HomeMissing,
            WireRefusal::HomeMissing,
        ),
        (HomeGroupRepairRefusal::Symlink, WireRefusal::Symlink),
        (
            HomeGroupRepairRefusal::NotADirectory,
            WireRefusal::NotADirectory,
        ),
        (
            HomeGroupRepairRefusal::DifferentMount,
            WireRefusal::DifferentMount,
        ),
        (
            HomeGroupRepairRefusal::OwnerMismatch,
            WireRefusal::OwnerMismatch,
        ),
    ];

    for (reason, expected) in reasons {
        let report = HomeGroupRepairReport {
            examined: 1,
            refused: vec![RefusedHome {
                account: account(),
                home: "/home/h_stco".to_owned(),
                reason,
            }],
            ..HomeGroupRepairReport::default()
        };

        let wire = wire_home_group_repair_report(report);

        assert_eq!(wire.refused[0].reason, expected as i32, "{reason}");
        assert_eq!(wire.refused[0].account_username, "h_stco");
        assert_eq!(wire.refused[0].home, "/home/h_stco");
    }
}
