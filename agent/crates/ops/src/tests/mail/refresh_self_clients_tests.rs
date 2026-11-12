//! Making the table that keeps this host's own users off port 25 follow the host.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::host_addresses::HostAddresses;

use crate::mail::disable_mail::disable_mail;
use crate::mail::enable_mail::enable_mail;
use crate::mail::fake_mail_host::{FakeMailHost, VERSION_ID, distro, settings};
use crate::mail::mail_error::MailError;
use crate::mail::mail_host::MailHost;
use crate::mail::model::mail_daemon::MailDaemon;
use crate::mail::model::self_clients_outcome::SelfClientsOutcome;
use crate::mail::refresh_self_clients::refresh_self_clients;

const TABLE: &str = "/etc/maran/mail/self_clients.cidr";

fn enabled_host() -> FakeMailHost {
    let host = FakeMailHost::new();
    enable_mail(&host, distro(), VERSION_ID, &settings()).unwrap();

    host
}

/// How many times the service manager was asked to reload (either verb) Postfix.
fn reloads(host: &FakeMailHost, verb: &str) -> usize {
    host.verbs_for("postfix")
        .iter()
        .filter(|seen| *seen == verb)
        .count()
}

/// The issue: an address gained after the last enable reaches the table, through
/// Postfix's check and a reload, with nothing else written.
#[test]
fn a_gained_address_is_written_checked_and_reloaded_without_another_file_changing() {
    let host = enabled_host();
    let writes = host.writes().len();
    let plain = reloads(&host, "reload");
    host.gain_address("198.51.100.7");

    let outcome = refresh_self_clients(&host, distro(), &host.host_addresses().unwrap());

    assert_eq!(outcome, Ok(SelfClientsOutcome::Rewritten));
    assert!(host.file(TABLE).unwrap().contains("198.51.100.7/32 REJECT"));
    let added = &host.writes()[writes..];
    assert_eq!(added.len(), 1);
    assert_eq!(added[0].daemon, MailDaemon::Postfix);
    assert_eq!(added[0].targets, [std::path::PathBuf::from(TABLE)]);
    assert_eq!(reloads(&host, "reload"), plain + 1);
}

/// D2: the refresh reloads with the plain verb, which the service manager refuses for a
/// stopped unit. `reload-or-restart` would start it.
#[test]
fn the_refresh_never_uses_the_verb_that_starts_a_stopped_unit() {
    let host = enabled_host();
    let restarting = reloads(&host, "reload-or-restart");
    host.gain_address("198.51.100.7");

    refresh_self_clients(&host, distro(), &host.host_addresses().unwrap()).unwrap();

    assert_eq!(reloads(&host, "reload-or-restart"), restarting);
}

/// A routed block reaches the table as a range.
#[test]
fn a_gained_local_range_is_written_as_a_range() {
    let host = enabled_host();
    host.gain_local_range("203.0.113.0", 24);

    refresh_self_clients(&host, distro(), &host.host_addresses().unwrap()).unwrap();

    assert!(host.file(TABLE).unwrap().contains("203.0.113.0/24 REJECT"));
}

/// Inverse control: an unchanged host writes nothing and reloads nothing.
#[test]
fn an_unchanged_host_writes_and_reloads_nothing() {
    let host = enabled_host();
    let writes = host.writes().len();
    let plain = reloads(&host, "reload");
    let restarting = reloads(&host, "reload-or-restart");

    let outcome = refresh_self_clients(&host, distro(), &host.host_addresses().unwrap());

    assert_eq!(outcome, Ok(SelfClientsOutcome::Unchanged));
    assert_eq!(host.writes().len(), writes);
    assert_eq!(reloads(&host, "reload"), plain);
    assert_eq!(reloads(&host, "reload-or-restart"), restarting);
}

/// Mail never enabled: no table is created.
#[test]
fn a_host_that_never_enabled_mail_gets_no_table() {
    let host = FakeMailHost::new();

    let outcome = refresh_self_clients(&host, distro(), &host.host_addresses().unwrap());

    assert_eq!(outcome, Ok(SelfClientsOutcome::NotEnabled));
    assert_eq!(host.file(TABLE), None);
    assert!(host.writes().is_empty());
}

/// D1 and D2: mail disabled (the table stays, Postfix is stopped). The table IS brought
/// to the host's addresses, so a Postfix started next reads the current ones, and
/// Postfix is neither reloaded nor started. The outcome is not "followed".
#[test]
fn a_stopped_postfix_gets_the_current_table_and_is_not_started() {
    let host = FakeMailHost::new().with_file(TABLE, "# an old table\n");
    host.gain_address("198.51.100.7");

    let outcome = refresh_self_clients(&host, distro(), &host.host_addresses().unwrap());

    assert_eq!(outcome, Ok(SelfClientsOutcome::NotRunning));
    assert!(host.file(TABLE).unwrap().contains("198.51.100.7/32 REJECT"));
    assert!(!host.is_active("postfix"));
    assert_eq!(reloads(&host, "reload"), 0);
    assert_eq!(reloads(&host, "reload-or-restart"), 0);
    assert_eq!(host.writes().len(), 1);
}

/// Inverse control: a stopped Postfix whose table is already current is not written
/// again, and still answers "not running" (so the caller keeps asking).
#[test]
fn a_stopped_postfix_with_a_current_table_is_not_written_again() {
    let host = FakeMailHost::new().with_file(TABLE, "# an old table\n");
    let addresses = host.host_addresses().unwrap();
    refresh_self_clients(&host, distro(), &addresses).unwrap();
    let writes = host.writes().len();

    let outcome = refresh_self_clients(&host, distro(), &addresses);

    assert_eq!(outcome, Ok(SelfClientsOutcome::NotRunning));
    assert_eq!(host.writes().len(), writes);
}

/// D2, the race: Postfix is found running and stops before the reload (a disable that
/// landed in the window). The reload is refused, the table is put back and NOTHING is
/// started. With `reload-or-restart` this test fails: the reload starts Postfix.
#[test]
fn a_postfix_that_stops_between_the_check_and_the_reload_is_not_started() {
    let host = FakeMailHost::new()
        .with_file(TABLE, "# an old table\n")
        .with_running_unit("postfix")
        .with_unit_stopping_after_check("postfix");
    host.gain_address("198.51.100.7");

    let outcome = refresh_self_clients(&host, distro(), &host.host_addresses().unwrap());

    assert!(matches!(outcome, Err(MailError::ServiceRefused { .. })));
    assert!(!host.is_active("postfix"), "Postfix stays stopped");
    assert_eq!(host.file(TABLE).as_deref(), Some("# an old table\n"));
}

/// D2, the serialisation itself: a refresh makes every call it makes to the host under the lifecycle
/// lock, so a start or stop in progress holds it off.
#[test]
fn a_refresh_works_under_the_lifecycle_lock() {
    let host = FakeMailHost::new()
        .with_file(TABLE, "# an old table\n")
        .with_running_unit("postfix");
    host.gain_address("198.51.100.7");
    let addresses = host.host_addresses().unwrap();

    let outcome = refresh_self_clients(&host, distro(), &addresses);

    assert_eq!(outcome, Ok(SelfClientsOutcome::Rewritten));
    let (under_the_lock, outside_it) = host.lifecycle_calls();
    assert!(under_the_lock >= 1, "the refresh never reached the host");
    assert_eq!(
        outside_it, 0,
        "the refresh called the host outside the lock"
    );
}

/// D2, the other side of the same lock: a disable runs under it, so it cannot land between a refresh's
/// check and its reload.
#[test]
fn a_disable_works_under_the_lifecycle_lock() {
    let host = enabled_host();

    let state = disable_mail(&host, distro(), VERSION_ID);

    assert!(state.is_ok());
    assert!(!host.is_active("postfix"));
    let (under_the_lock, outside_it) = host.lifecycle_calls();
    assert!(under_the_lock >= 1, "the disable never reached the host");
    assert_eq!(
        outside_it, 0,
        "the disable called the host outside the lock"
    );
}

/// And an enable works under it likewise.
#[test]
fn an_enable_works_under_the_lifecycle_lock() {
    let host = FakeMailHost::new();

    let state = enable_mail(&host, distro(), VERSION_ID, &settings());

    assert!(state.is_ok());
    assert!(host.is_active("postfix"));
    let (under_the_lock, outside_it) = host.lifecycle_calls();
    assert!(under_the_lock >= 1, "the enable never reached the host");
    assert_eq!(outside_it, 0, "the enable called the host outside the lock");
}

/// The control of the observation: a call made while this thread holds the lifecycle lock is counted as
/// made under it, so the zero above cannot be a probe that never says "outside".
#[test]
fn a_call_made_while_the_lock_is_held_is_counted_as_held() {
    let host = FakeMailHost::new();
    let _guard = crate::mail::mail_lifecycle_lock::hold_mail_lifecycle();

    let _ = host.read_config(std::path::Path::new(TABLE));

    assert_eq!(host.lifecycle_calls(), (1, 0));
}

/// Postfix's own check refuses the table: the old one is put back.
#[test]
fn a_table_postfix_refuses_is_put_back() {
    let host = FakeMailHost::new()
        .with_file(TABLE, "# an old table\n")
        .with_running_unit("postfix")
        .with_refusing_check(MailDaemon::Postfix, "bad table");

    let outcome = refresh_self_clients(&host, distro(), &HostAddresses::default());

    assert!(matches!(outcome, Err(MailError::ConfigRejected { .. })));
    assert_eq!(host.file(TABLE).as_deref(), Some("# an old table\n"));
}
