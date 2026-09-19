//! The grant-repair report as the contract carries it.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::db::db_user_name::DbUserName;
use maran_agent_core::validation::system::name::AccountName;
use maran_ops::db::{GrantRepairRefusal, GrantRepairReport, RefusedGrant, RepairedGrant};

use super::wire_grant_repair_report;
use crate::proto::GrantRepairRefusal as WireRefusal;

/// The account the repaired row in these tests belongs to.
fn account() -> AccountName {
    AccountName::parse("h_stco").expect("valid")
}

/// A repaired grant whose old pattern also reached one neighbour.
fn repaired() -> RepairedGrant {
    RepairedGrant {
        database: DatabaseName::for_account(&account(), "main").expect("valid"),
        user: DbUserName::for_account(&account(), "main").expect("valid"),
        also_matched: vec!["hostco_main".to_owned()],
    }
}

/// A repaired row reaches the wire with both names and the collision it found.
#[test]
fn a_repaired_row_carries_its_names_and_the_databases_its_pattern_also_reached() {
    let report = GrantRepairReport {
        examined: 2,
        already_correct: 1,
        repaired: vec![repaired()],
        ..GrantRepairReport::default()
    };

    let wire = wire_grant_repair_report(report);

    assert_eq!(wire.examined_grants, 2);
    assert_eq!(wire.already_correct, 1);
    assert_eq!(wire.repaired[0].database_name, "h_stco_main");
    assert_eq!(wire.repaired[0].db_username, "h_stco_main");
    assert_eq!(
        wire.repaired[0].also_matched_databases,
        vec!["hostco_main".to_owned()],
        "the collision is the only evidence this operation can produce, so it \
         must survive the trip to the panel"
    );
    assert!(wire.would_repair.is_empty());
}

/// A report-only pass fills `would_repair` and leaves `repaired` empty.
#[test]
fn a_report_only_pass_fills_would_repair_and_nothing_else() {
    let report = GrantRepairReport {
        examined: 1,
        would_repair: vec![repaired()],
        ..GrantRepairReport::default()
    };

    let wire = wire_grant_repair_report(report);

    assert!(wire.repaired.is_empty());
    assert_eq!(wire.would_repair.len(), 1);
}

/// Every refusal reason maps onto its own contract value.
///
/// One case each, because the reasons are what an operator acts on and two of
/// them collapsing onto one value would send them to the wrong row. The raw
/// columns travel untouched: a refused row is refused precisely because it is not
/// a value this agent could have produced.
#[test]
fn every_refusal_reason_reaches_the_wire_as_its_own_value() {
    let reasons = [
        (
            GrantRepairRefusal::HostIsNotLocalhost,
            WireRefusal::HostIsNotLocalhost,
        ),
        (
            GrantRepairRefusal::NotThePanelsNaming,
            WireRefusal::NotThePanelsNaming,
        ),
        (
            GrantRepairRefusal::UnrecognisedPrivileges,
            WireRefusal::UnrecognisedPrivileges,
        ),
        (
            GrantRepairRefusal::PartiallyOrUnfamiliarlyEscaped,
            WireRefusal::PartiallyOrUnfamiliarlyEscaped,
        ),
    ];

    for (reason, expected) in reasons {
        let report = GrantRepairReport {
            examined: 1,
            refused: vec![RefusedGrant {
                host: "%".to_owned(),
                database: "h_stco_main".to_owned(),
                user: "reporting_tool".to_owned(),
                reason,
            }],
            ..GrantRepairReport::default()
        };

        let wire = wire_grant_repair_report(report);

        assert_eq!(wire.refused[0].reason, expected as i32, "{reason}");
        assert_eq!(wire.refused[0].grant_host, "%");
        assert_eq!(wire.refused[0].database_name, "h_stco_main");
        assert_eq!(wire.refused[0].db_username, "reporting_tool");
    }
}
