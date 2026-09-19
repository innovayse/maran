//! Repairing the pattern grants older agents issued, and refusing everything
//! else.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::repair_grants;
use crate::db::create_database::create_database;
use crate::db::db_error::DbError;
use crate::db::fake_db_host::{FakeDbHost, shop_request};
use crate::db::model::grant_repair_refusal::GrantRepairRefusal;

/// The account both colliding names in this file belong to.
///
/// **The geometry is the point.** `h_stco` and `hostco` are the SAME LENGTH and
/// differ at exactly one position — index 1 — where the first holds the
/// separator. A pattern whose only metacharacter is `_` matches one character per
/// `_`, so it matches only strings of its own length: two names of different
/// lengths cannot collide however many separators they carry, which is why the
/// pair here is chosen and not borrowed from the other fixtures.
const COLLIDING_ATTACKER_DATABASE: &str = "h_stco_main";

/// The victim's database, matched character for character by the attacker's
/// unescaped pattern.
const COLLIDING_VICTIM_DATABASE: &str = "hostco_main";

/// How the server renders the grant `create_database` issues on `database`.
fn rendered_all_privileges(database: &str, user: &str) -> String {
    format!("GRANT ALL PRIVILEGES ON `{database}`.* TO `{user}`@`localhost`")
}

/// A host holding the two colliding databases and the attacker's unescaped row.
fn host_with_the_defective_row() -> FakeDbHost {
    let host = FakeDbHost::with_existing_many(&[
        COLLIDING_VICTIM_DATABASE,
        COLLIDING_ATTACKER_DATABASE,
        "mysql",
    ]);
    host.add_grant_row(
        "localhost",
        COLLIDING_ATTACKER_DATABASE,
        COLLIDING_ATTACKER_DATABASE,
        &rendered_all_privileges(COLLIDING_ATTACKER_DATABASE, COLLIDING_ATTACKER_DATABASE),
    );

    host
}

/// The escaped grant is issued BEFORE the wide one is revoked.
#[test]
fn the_escaped_grant_is_issued_before_the_unescaped_one_is_revoked() {
    let host = host_with_the_defective_row();

    let report = repair_grants(&host, false).expect("the pass must succeed");

    assert_eq!(report.repaired.len(), 1);
    assert_eq!(report.refused, Vec::new());
    let issued: Vec<String> = host
        .statements()
        .into_iter()
        .filter(|statement| statement.starts_with("GRANT ") || statement.starts_with("REVOKE "))
        .collect();
    assert_eq!(
        issued,
        vec![
            "GRANT ALL PRIVILEGES ON `h\\_stco\\_main`.* TO 'h_stco_main'@'localhost'".to_owned(),
            "REVOKE ALL PRIVILEGES ON `h_stco_main`.* FROM 'h_stco_main'@'localhost'".to_owned(),
        ],
        "the grant must come first: a crash between the two must leave the \
         customer with access, not without it"
    );
    assert_eq!(
        host.granted_databases(),
        vec!["h\\_stco\\_main".to_owned()],
        "the wide row must be gone and the escaped one must be the only one left"
    );
}

/// A repaired row names the other databases its old pattern also reached.
#[test]
fn a_repaired_row_names_the_other_databases_its_pattern_also_reached() {
    let host = host_with_the_defective_row();

    let report = repair_grants(&host, false).expect("the pass must succeed");

    assert_eq!(
        report.repaired[0].also_matched,
        vec![COLLIDING_VICTIM_DATABASE.to_owned()],
        "the collision is the one piece of evidence this operation can produce"
    );
}

/// A second pass over a repaired host sends no DDL at all.
#[test]
fn a_second_pass_over_a_repaired_host_changes_nothing() {
    let host = host_with_the_defective_row();
    repair_grants(&host, false).expect("the first pass must succeed");

    let again = repair_grants(&host, true).expect("the second pass must succeed");
    let report = repair_grants(&host, false).expect("the third pass must succeed");

    assert_eq!(again.would_repair, Vec::new());
    assert_eq!(report.repaired, Vec::new());
    assert_eq!(report.refused, Vec::new());
    assert_eq!(report.already_correct, report.examined);
    assert_eq!(
        host.granted_databases(),
        vec!["h\\_stco\\_main".to_owned()],
        "the escaped row must not be re-escaped: a pass that mistook its own \
         output for a broken row would rewrite every grant on every run"
    );
}

/// A grant this agent has just issued is already correct.
///
/// The anti-drift check, and it is why it goes through `create_database` rather
/// than through a hand-written row: the repair's predicate is "this is not what
/// the escape produces", so the escape and the predicate disagreeing would make
/// every freshly created database look broken. Nothing else in the suite would
/// notice.
#[test]
fn a_grant_create_database_has_just_issued_is_reported_as_already_correct() {
    let host = FakeDbHost::new();
    create_database(&host, &shop_request()).expect("the create must succeed");

    let report = repair_grants(&host, false).expect("the pass must succeed");

    assert_eq!(report.examined, 1);
    assert_eq!(report.already_correct, 1);
    assert_eq!(report.repaired, Vec::new());
    assert_eq!(report.refused, Vec::new());
}

/// A report-only pass lists what it would do and sends no DDL.
#[test]
fn a_report_only_pass_lists_what_it_would_do_and_sends_no_ddl() {
    let host = host_with_the_defective_row();

    let report = repair_grants(&host, true).expect("the pass must succeed");

    assert_eq!(report.would_repair.len(), 1);
    assert_eq!(report.repaired, Vec::new());
    assert_eq!(
        report.would_repair[0].database.as_str(),
        COLLIDING_ATTACKER_DATABASE
    );
    assert!(
        !host
            .statements()
            .iter()
            .any(|statement| statement.starts_with("GRANT ") || statement.starts_with("REVOKE ")),
        "an operation that can take a customer's access away must be inspectable \
         before it does"
    );
    assert_eq!(host.granted_databases(), vec![COLLIDING_ATTACKER_DATABASE]);
}

/// A row granted from another host is refused and left exactly as it was.
#[test]
fn a_row_granted_from_another_host_is_refused() {
    let host = FakeDbHost::with_existing("hostco_main");
    host.add_grant_row(
        "%",
        "h_stco_main",
        "h_stco_main",
        &rendered_all_privileges("h_stco_main", "h_stco_main"),
    );

    let report = repair_grants(&host, false).expect("the pass must succeed");

    assert_eq!(report.repaired, Vec::new());
    assert_eq!(report.refused.len(), 1);
    assert_eq!(
        report.refused[0].reason,
        GrantRepairRefusal::HostIsNotLocalhost
    );
    assert_eq!(host.granted_databases(), vec!["h_stco_main".to_owned()]);
}

/// A row pairing a stranger's user with a panel-shaped database is refused.
#[test]
fn a_row_pairing_a_strangers_user_with_a_panel_database_is_refused() {
    let host = FakeDbHost::with_existing("h_stco_main");
    host.add_grant_row(
        "localhost",
        "h_stco_main",
        "reporting_tool",
        &rendered_all_privileges("h_stco_main", "reporting_tool"),
    );

    let report = repair_grants(&host, false).expect("the pass must succeed");

    assert_eq!(report.repaired, Vec::new());
    assert_eq!(
        report.refused[0].reason,
        GrantRepairRefusal::NotThePanelsNaming
    );
    assert_eq!(host.granted_databases(), vec!["h_stco_main".to_owned()]);
}

/// A narrower grant on a panel-shaped name is refused, not widened.
#[test]
fn a_narrower_grant_on_a_panel_shaped_name_is_refused_rather_than_widened() {
    let host = FakeDbHost::with_existing("h_stco_main");
    host.add_grant_row(
        "localhost",
        "h_stco_main",
        "h_stco_main",
        "GRANT SELECT ON `h_stco_main`.* TO `h_stco_main`@`localhost`",
    );

    let report = repair_grants(&host, false).expect("the pass must succeed");

    assert_eq!(report.repaired, Vec::new());
    assert_eq!(
        report.refused[0].reason,
        GrantRepairRefusal::UnrecognisedPrivileges,
        "re-granting ALL PRIVILEGES over a read-only grant would escalate it, \
         which is worse than the wildcard it came to fix"
    );
    assert_eq!(host.granted_databases(), vec!["h_stco_main".to_owned()]);
}

/// A pattern escaped in a form this panel never writes is refused.
#[test]
fn a_pattern_escaped_in_an_unfamiliar_form_is_refused() {
    let host = FakeDbHost::with_existing("h_stco_main");
    host.add_grant_row(
        "localhost",
        "h_stco\\_main",
        "h_stco_main",
        &rendered_all_privileges("h_stco\\_main", "h_stco_main"),
    );

    let report = repair_grants(&host, false).expect("the pass must succeed");

    assert_eq!(report.repaired, Vec::new());
    assert_eq!(
        report.refused[0].reason,
        GrantRepairRefusal::PartiallyOrUnfamiliarlyEscaped
    );
    assert_eq!(host.granted_databases(), vec!["h_stco\\_main".to_owned()]);
}

/// A name that cannot carry a wildcard is neither repaired nor reported.
#[test]
fn a_name_with_no_separator_is_counted_correct_and_not_reported() {
    let host = FakeDbHost::with_existing("reporting");
    host.add_grant_row(
        "localhost",
        "reporting",
        "analyst",
        "GRANT ALL PRIVILEGES ON `reporting`.* TO `analyst`@`localhost`",
    );

    let report = repair_grants(&host, false).expect("the pass must succeed");

    assert_eq!(report.already_correct, 1);
    assert_eq!(report.refused, Vec::new());
    assert_eq!(report.repaired, Vec::new());
}

/// A grant-table row that is not three fields is refused, not guessed at.
#[test]
fn a_grant_row_the_server_printed_short_is_unparsable() {
    let host = FakeDbHost::new();
    host.set_grant_rows_output("localhost\th_stco_main");

    assert_eq!(repair_grants(&host, false), Err(DbError::Unparsable));
}
