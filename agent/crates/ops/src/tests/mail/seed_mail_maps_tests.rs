//! Creating the lookup tables a mail stack with no customers still needs.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::mail::fake_mail_host::{FakeMailHost, distro};
use crate::mail::model::mail_daemon::MailDaemon;
use crate::mail::seed_mail_maps::seed_mail_maps;

/// How many of [`TABLES`] are Postfix lookup tables, which `postmap` indexes.
const POSTFIX_TABLES: usize = 5;

const TABLES: [&str; 9] = [
    "/etc/maran/mail/virtual_domains",
    "/etc/maran/mail/virtual_mailboxes",
    "/etc/maran/mail/virtual_aliases",
    "/etc/maran/mail/sender_login_maps",
    "/etc/postfix/maran_local_submit_users",
    "/etc/maran/mail/local_submit_accounts.map",
    "/etc/maran/mail/account_sender_domains.map",
    "/etc/maran/mail/account_send_limits.map",
    "/etc/maran/mail/mailbox_accounts.map",
];

#[test]
fn every_table_is_created_empty_on_a_host_that_has_none() {
    let host = FakeMailHost::new();

    seed_mail_maps(&host, distro()).unwrap();

    for table in TABLES {
        // The four Rspamd maps hold a comment line (Rspamd does not refresh a map
        // from zero bytes); the Postfix tables stay empty.
        let expected = if table.ends_with(".map") {
            "# no entries\n"
        } else {
            ""
        };
        assert_eq!(host.file(table).as_deref(), Some(expected), "{table}");
    }
    assert_eq!(
        host.file("/etc/maran/mail/dovecot/users").as_deref(),
        Some("")
    );
    assert_eq!(
        host.file("/etc/maran/mail/dkim/selectors").as_deref(),
        Some("")
    );
}

#[test]
fn a_table_that_already_holds_data_is_never_touched() {
    // A populated table is a customer's mail routing; an enable is not the
    // operation that re-pushes it.
    let host = FakeMailHost::new()
        .with_file("/etc/maran/mail/virtual_domains", "example.com OK\n")
        .with_file(
            "/etc/maran/mail/dovecot/users",
            "box@example.com:{BLF-CRYPT}x\n",
        );

    seed_mail_maps(&host, distro()).unwrap();

    assert_eq!(
        host.file("/etc/maran/mail/virtual_domains").as_deref(),
        Some("example.com OK\n")
    );
    assert_eq!(
        host.file("/etc/maran/mail/dovecot/users").as_deref(),
        Some("box@example.com:{BLF-CRYPT}x\n")
    );
    let written: Vec<_> = host
        .writes()
        .into_iter()
        .flat_map(|write| write.targets)
        .map(|target| target.to_string_lossy().into_owned())
        .collect();
    assert!(!written.contains(&"/etc/maran/mail/virtual_domains".to_owned()));
}

#[test]
fn a_postfix_table_is_indexed_by_postmap_in_the_hosts_map_type() {
    let host = FakeMailHost::new();

    seed_mail_maps(&host, distro()).unwrap();

    let indexed: Vec<_> = host
        .spawns()
        .into_iter()
        .filter(|spawn| spawn.argv[0] == distro().postmap_binary().as_str())
        .map(|spawn| spawn.argv[1].clone())
        .collect();
    // The first five are Postfix's lookup tables; the Rspamd and Dovecot files after them are
    // read as text and have no index.
    let expected: Vec<String> = TABLES[..POSTFIX_TABLES]
        .iter()
        .map(|table| format!("lmdb:{table}"))
        .collect();
    assert_eq!(indexed, expected);
}

#[test]
fn the_dovecot_and_rspamd_files_are_checked_by_their_own_daemons() {
    let host = FakeMailHost::new();

    seed_mail_maps(&host, distro()).unwrap();

    let programs: Vec<_> = host
        .spawns()
        .into_iter()
        .map(|spawn| spawn.argv[0].clone())
        .collect();
    assert!(programs.contains(&distro().doveconf_binary().as_str().to_owned()));
    assert!(programs.contains(&distro().rspamadm_binary().as_str().to_owned()));
    assert!(
        host.writes()
            .iter()
            .any(|write| write.daemon == MailDaemon::Dovecot)
    );
    assert!(
        host.writes()
            .iter()
            .any(|write| write.daemon == MailDaemon::Rspamd)
    );
}

#[test]
fn a_populated_local_submission_map_is_never_touched() {
    // Inverse control of the seeding: an account already allowed to submit
    // locally keeps its line across an enable.
    let host =
        FakeMailHost::new().with_file("/etc/maran/mail/local_submit_accounts.map", "1001 acme\n");

    seed_mail_maps(&host, distro()).unwrap();

    assert_eq!(
        host.file("/etc/maran/mail/local_submit_accounts.map")
            .as_deref(),
        Some("1001 acme\n")
    );
}
