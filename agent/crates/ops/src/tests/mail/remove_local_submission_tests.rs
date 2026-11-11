//! Taking an account out of local submission.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::mail::fake_mail_host::{FakeMailHost, distro};
use crate::mail::mail_error::MailError;
use crate::mail::model::mail_daemon::MailDaemon;
use crate::mail::remove_local_submission::remove_local_submission;

fn host_with_two_accounts() -> FakeMailHost {
    FakeMailHost::new()
        .enabled()
        .with_file(
            "/etc/maran/mail/local_submit_accounts.map",
            "1001 acme\n1002 other\n",
        )
        .with_file(
            "/etc/maran/mail/account_sender_domains.map",
            "acme a.test\nacme b.test\nother o.test\n",
        )
        .with_file(
            "/etc/maran/mail/account_send_limits.map",
            "acme 200\nother 9\n",
        )
        .with_file(
            "/etc/maran/mail/mailbox_accounts.map",
            "info@a.test acme\ninfo@o.test other\n",
        )
        .with_file(
            "/etc/postfix/maran_local_submit_users",
            "acme OK\nother OK\n",
        )
}

#[test]
fn every_line_of_the_account_goes_and_the_other_accounts_stay() {
    let host = host_with_two_accounts();

    remove_local_submission(&host, distro(), "acme").unwrap();

    assert_eq!(
        host.file("/etc/maran/mail/local_submit_accounts.map")
            .as_deref(),
        Some("1002 other\n")
    );
    assert_eq!(
        host.file("/etc/maran/mail/account_sender_domains.map")
            .as_deref(),
        Some("other o.test\n")
    );
    assert_eq!(
        host.file("/etc/maran/mail/account_send_limits.map")
            .as_deref(),
        Some("other 9\n")
    );
    assert_eq!(
        host.file("/etc/maran/mail/mailbox_accounts.map").as_deref(),
        Some("info@o.test other\n")
    );
    assert_eq!(
        host.file("/etc/postfix/maran_local_submit_users")
            .as_deref(),
        Some("other OK\n")
    );
}

#[test]
fn postdrop_stops_admitting_the_account_before_the_filter_forgets_it() {
    let host = host_with_two_accounts();

    remove_local_submission(&host, distro(), "acme").unwrap();

    let order: Vec<MailDaemon> = host.writes().iter().map(|write| write.daemon).collect();
    assert_eq!(order, [MailDaemon::Postfix, MailDaemon::Rspamd]);
}

#[test]
fn an_unknown_account_changes_no_line() {
    // Inverse control: removing someone who is not there keeps every line.
    let host = host_with_two_accounts();

    remove_local_submission(&host, distro(), "ghost").unwrap();

    assert_eq!(
        host.file("/etc/maran/mail/account_send_limits.map")
            .as_deref(),
        Some("acme 200\nother 9\n")
    );
}

#[test]
fn a_refused_filter_check_is_reported() {
    let host = host_with_two_accounts().with_refusing_check(MailDaemon::Rspamd, "bad map");

    let error = remove_local_submission(&host, distro(), "acme").unwrap_err();

    assert!(matches!(error, MailError::ConfigRejected { .. }));
    assert_eq!(
        host.file("/etc/maran/mail/account_send_limits.map")
            .as_deref(),
        Some("acme 200\nother 9\n")
    );
}

#[test]
fn the_last_account_leaves_a_comment_line_so_rspamd_refreshes_its_maps() {
    // Zero bytes would leave the filter holding the previous, populated copy.
    let host = FakeMailHost::new()
        .enabled()
        .with_file("/etc/maran/mail/local_submit_accounts.map", "1001 acme\n")
        .with_file(
            "/etc/maran/mail/account_sender_domains.map",
            "acme a.test\n",
        )
        .with_file("/etc/maran/mail/account_send_limits.map", "acme 200\n")
        .with_file("/etc/maran/mail/mailbox_accounts.map", "info@a.test acme\n")
        .with_file("/etc/postfix/maran_local_submit_users", "acme OK\n");

    remove_local_submission(&host, distro(), "acme").unwrap();

    for table in [
        "/etc/maran/mail/local_submit_accounts.map",
        "/etc/maran/mail/account_sender_domains.map",
        "/etc/maran/mail/account_send_limits.map",
        "/etc/maran/mail/mailbox_accounts.map",
        "/etc/postfix/maran_local_submit_users",
    ] {
        let contents = host.file(table).unwrap();
        assert!(!contents.is_empty(), "{table} was written as zero bytes");
        assert!(
            contents.lines().all(|line| line.starts_with('#')),
            "{table}"
        );
    }
}
