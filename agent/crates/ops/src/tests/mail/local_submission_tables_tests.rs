//! Replacing one account's lines in a lookup table.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::mail::fake_mail_host::{FakeMailHost, distro};
use crate::mail::local_submission_tables::{
    AccountLines, EMPTY_TABLE, apply_account_lines, hold_tables, replace_account_lines,
};
use crate::mail::mail_error::MailError;
use crate::mail::model::local_submission_key::LocalSubmissionKey;

#[test]
fn the_accounts_old_lines_are_replaced_and_the_others_kept() {
    let current = "acme a.example\nacme b.example\nother c.example\n";

    let rendered = replace_account_lines(current, 0, "acme", &["acme d.example".to_owned()]);

    assert_eq!(rendered, "acme d.example\nother c.example\n");
}

#[test]
fn a_table_keyed_by_uid_is_matched_on_its_second_field() {
    let current = "1001 acme\n1002 other\n";

    let rendered = replace_account_lines(current, 1, "acme", &[]);

    assert_eq!(rendered, "1002 other\n");
}

#[test]
fn an_account_that_is_a_prefix_of_another_is_not_confused_with_it() {
    // Inverse control: matching is by whole field, not by prefix.
    let current = "acme a.example\nacme2 b.example\n";

    let rendered = replace_account_lines(current, 0, "acme", &[]);

    assert_eq!(rendered, "acme2 b.example\n");
}

#[test]
fn the_same_push_twice_renders_the_same_bytes() {
    let added = vec!["acme b.example".to_owned(), "acme a.example".to_owned()];

    let once = replace_account_lines("", 0, "acme", &added);
    let twice = replace_account_lines(&once, 0, "acme", &added);

    assert_eq!(once, "acme a.example\nacme b.example\n");
    assert_eq!(once, twice);
}

#[test]
fn removing_the_only_account_leaves_the_comment_line_not_zero_bytes() {
    assert_eq!(
        replace_account_lines("acme OK\n", 0, "acme", &[]),
        EMPTY_TABLE
    );
}

#[test]
fn a_comment_line_is_never_kept_as_data() {
    // Inverse control of the comment line above: once the table has a real line
    // again the placeholder is gone.
    let rendered = replace_account_lines(EMPTY_TABLE, 0, "acme", &["acme OK".to_owned()]);

    assert_eq!(rendered, "acme OK\n");
}

const ACCOUNTS: &str = "/etc/maran/mail/local_submit_accounts.map";
const DOMAINS: &str = "/etc/maran/mail/account_sender_domains.map";
const LOGINS: &str = "/etc/maran/mail/mailbox_accounts.map";

fn lines(uid: &str, account: &str, domain: &str, login: &str) -> AccountLines {
    AccountLines {
        local_accounts: vec![format!("{uid} {account}")],
        sender_domains: vec![format!("{account} {domain}")],
        send_limits: vec![format!("{account} 10")],
        mailbox_accounts: vec![format!("{login} {account}")],
        submit_users: vec![format!("{account} OK")],
    }
}

#[test]
fn a_uid_bound_to_a_new_account_is_taken_from_the_old_one() {
    // The newest binding wins for a uid: a deleted account's surviving line must
    // not answer for the account that reuses its uid.
    let host = FakeMailHost::new()
        .enabled()
        .with_file(ACCOUNTS, "1001 ghost\n1002 other\n");

    apply_account_lines(
        &host,
        distro(),
        "acme",
        &lines("1001", "acme", "a.test", "i@a.test"),
        true,
    )
    .unwrap();

    assert_eq!(
        host.file(ACCOUNTS).as_deref(),
        Some("1001 acme\n1002 other\n")
    );
}

#[test]
fn a_different_uid_leaves_the_other_accounts_uid_alone() {
    // Inverse control of the uid takeover.
    let host = FakeMailHost::new()
        .enabled()
        .with_file(ACCOUNTS, "1005 ghost\n");

    apply_account_lines(
        &host,
        distro(),
        "acme",
        &lines("1001", "acme", "a.test", "i@a.test"),
        true,
    )
    .unwrap();

    assert_eq!(
        host.file(ACCOUNTS).as_deref(),
        Some("1001 acme\n1005 ghost\n")
    );
}

#[test]
fn a_domain_owned_by_another_account_is_a_conflict_and_nothing_is_written() {
    let host = FakeMailHost::new()
        .enabled()
        .with_file(DOMAINS, "other A.Test\n");

    let error = apply_account_lines(
        &host,
        distro(),
        "acme",
        &lines("1001", "acme", "a.test", "i@a.test"),
        true,
    )
    .unwrap_err();

    assert_eq!(
        error,
        MailError::LocalSubmissionConflict {
            key: LocalSubmissionKey::SenderDomain,
            value: "A.Test".to_owned()
        }
    );
    assert!(host.writes().is_empty());
}

#[test]
fn a_login_owned_by_another_account_is_a_conflict_and_nothing_is_written() {
    let host = FakeMailHost::new()
        .enabled()
        .with_file(LOGINS, "i@a.test other\n");

    let error = apply_account_lines(
        &host,
        distro(),
        "acme",
        &lines("1001", "acme", "b.test", "i@a.test"),
        true,
    )
    .unwrap_err();

    assert!(matches!(
        error,
        MailError::LocalSubmissionConflict {
            key: LocalSubmissionKey::MailboxLogin,
            ..
        }
    ));
    assert!(host.writes().is_empty());
}

#[test]
fn a_domain_and_login_the_account_already_owns_are_not_a_conflict() {
    // Inverse control: pushing the same binding again, next to another account's
    // own domain and login, is accepted.
    let host = FakeMailHost::new()
        .enabled()
        .with_file(DOMAINS, "acme a.test\nother o.test\n")
        .with_file(LOGINS, "i@a.test acme\ni@o.test other\n");

    apply_account_lines(
        &host,
        distro(),
        "acme",
        &lines("1001", "acme", "a.test", "i@a.test"),
        true,
    )
    .unwrap();

    assert_eq!(
        host.file(DOMAINS).as_deref(),
        Some("acme a.test\nother o.test\n")
    );
    assert_eq!(
        host.file(LOGINS).as_deref(),
        Some("i@a.test acme\ni@o.test other\n")
    );
}

#[test]
fn removal_is_never_a_conflict() {
    let host = FakeMailHost::new()
        .enabled()
        .with_file(DOMAINS, "other a.test\n");

    apply_account_lines(&host, distro(), "acme", &AccountLines::default(), false).unwrap();

    assert_eq!(host.file(DOMAINS).as_deref(), Some("other a.test\n"));
}

#[test]
fn a_filter_that_does_not_report_the_rule_blocks_the_admission_of_the_account() {
    let host = FakeMailHost::new().enabled().with_unloaded_rule();

    let error = apply_account_lines(
        &host,
        distro(),
        "acme",
        &lines("1001", "acme", "a.test", "i@a.test"),
        true,
    )
    .unwrap_err();

    assert!(matches!(error, MailError::UnsafeLocalSubmission { .. }));
    // The filter knows the account; postdrop was never told to admit it.
    assert_eq!(host.file("/etc/postfix/maran_local_submit_users"), None);
    assert_eq!(host.file(ACCOUNTS).as_deref(), Some("1001 acme\n"));
}

#[test]
fn a_filter_that_reports_the_rule_lets_the_account_be_admitted() {
    // Inverse control of the refusal above.
    let host = FakeMailHost::new().enabled();

    apply_account_lines(
        &host,
        distro(),
        "acme",
        &lines("1001", "acme", "a.test", "i@a.test"),
        true,
    )
    .unwrap();

    assert_eq!(
        host.file("/etc/postfix/maran_local_submit_users")
            .as_deref(),
        Some("acme OK\n")
    );
}

/// A host whose reads wait for a signal, so a second change can be started between
/// a first change's read and its write.
#[test]
fn two_changes_cannot_interleave_a_read_with_the_others_write() {
    use std::sync::Arc;
    use std::sync::atomic::{AtomicBool, Ordering};

    let host = Arc::new(
        FakeMailHost::new()
            .enabled()
            .with_file(ACCOUNTS, "1001 acme\n"),
    );
    let inside = Arc::new(AtomicBool::new(false));

    // Hold the tables as a change in the middle of its read-render-write does.
    let guard = hold_tables();
    let removing = {
        let host = Arc::clone(&host);
        let inside = Arc::clone(&inside);
        std::thread::spawn(move || {
            inside.store(true, Ordering::SeqCst);
            apply_account_lines(
                host.as_ref(),
                distro(),
                "acme",
                &AccountLines::default(),
                false,
            )
        })
    };
    while !inside.load(Ordering::SeqCst) {
        std::thread::yield_now();
    }
    std::thread::sleep(std::time::Duration::from_millis(100));

    // The second change has not read or written anything while the first holds.
    assert!(host.writes().is_empty());
    assert_eq!(host.file(ACCOUNTS).as_deref(), Some("1001 acme\n"));
    drop(guard);

    removing.join().unwrap().unwrap();
    assert_eq!(host.file(ACCOUNTS).as_deref(), Some(EMPTY_TABLE));
}
