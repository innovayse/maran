//! Binding an account's local submission.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::mail::local_submission::LocalSubmission;
use maran_agent_core::validation::mail::mail_address::MailAddress;
use maran_agent_core::validation::mail::mail_domain::MailDomain;
use maran_agent_core::validation::mail::send_limit_per_hour::SendLimitPerHour;
use maran_agent_core::validation::system::name::AccountName;

use crate::mail::fake_mail_host::{FakeMailHost, distro};
use crate::mail::mail_error::MailError;
use crate::mail::model::local_submission_hazard::LocalSubmissionHazard;
use crate::mail::model::mail_daemon::MailDaemon;
use crate::mail::model::path_facts::PathFacts;
use crate::mail::set_local_submission::set_local_submission;

const ACCOUNTS: &str = "/etc/maran/mail/local_submit_accounts.map";
const DOMAINS: &str = "/etc/maran/mail/account_sender_domains.map";
const LIMITS: &str = "/etc/maran/mail/account_send_limits.map";
const LOGINS: &str = "/etc/maran/mail/mailbox_accounts.map";
const USERS: &str = "/etc/postfix/maran_local_submit_users";

fn submission(account: &str, uid: u32, domains: &[&str], limit: u32) -> LocalSubmission {
    LocalSubmission {
        account: AccountName::parse(account).unwrap(),
        uid,
        domains: domains
            .iter()
            .map(|domain| MailDomain::parse(domain).unwrap())
            .collect(),
        send_limit_per_hour: SendLimitPerHour::parse(limit).unwrap(),
        mailbox_logins: vec![MailAddress::parse(&format!("info@{}", domains[0])).unwrap()],
    }
}

#[test]
fn every_table_receives_the_accounts_lines() {
    let host = FakeMailHost::new().enabled();

    set_local_submission(
        &host,
        distro(),
        &submission("acme", 1001, &["a.test", "b.test"], 200),
    )
    .unwrap();

    assert_eq!(host.file(ACCOUNTS).as_deref(), Some("1001 acme\n"));
    assert_eq!(
        host.file(DOMAINS).as_deref(),
        Some("acme a.test\nacme b.test\n")
    );
    assert_eq!(host.file(LIMITS).as_deref(), Some("acme 200\n"));
    assert_eq!(host.file(LOGINS).as_deref(), Some("info@a.test acme\n"));
    assert_eq!(host.file(USERS).as_deref(), Some("acme OK\n"));
}

#[test]
fn a_second_push_replaces_the_account_as_a_unit_and_keeps_the_others() {
    let host = FakeMailHost::new()
        .enabled()
        .with_file(DOMAINS, "acme old.test\nother o.test\n")
        .with_file(ACCOUNTS, "1001 acme\n1002 other\n")
        .with_file(LIMITS, "acme 5\nother 9\n");

    set_local_submission(
        &host,
        distro(),
        &submission("acme", 1001, &["new.test"], 200),
    )
    .unwrap();

    assert_eq!(
        host.file(DOMAINS).as_deref(),
        Some("acme new.test\nother o.test\n")
    );
    assert_eq!(
        host.file(ACCOUNTS).as_deref(),
        Some("1001 acme\n1002 other\n")
    );
    assert_eq!(host.file(LIMITS).as_deref(), Some("acme 200\nother 9\n"));
}

#[test]
fn the_filter_is_validated_reloaded_and_the_postfix_table_indexed() {
    let host = FakeMailHost::new().enabled();

    set_local_submission(&host, distro(), &submission("acme", 1001, &["a.test"], 10)).unwrap();

    let writes = host.writes();
    assert_eq!(writes[0].daemon, MailDaemon::Rspamd);
    assert_eq!(writes[0].targets.len(), 4);
    assert_eq!(writes[1].daemon, MailDaemon::Postfix);
    assert!(
        host.spawns()
            .iter()
            .any(|spawn| spawn.argv[0] == distro().postmap_binary().as_str()
                && spawn.argv[1] == format!("lmdb:{USERS}"))
    );
    assert!(
        host.verbs_for(distro().rspamd_service())
            .contains(&"reload-or-restart".to_owned())
    );
}

#[test]
fn a_refused_filter_check_leaves_postfix_unadmitting_and_the_maps_restored() {
    let host = FakeMailHost::new()
        .enabled()
        .with_refusing_check(MailDaemon::Rspamd, "bad map");

    let error = set_local_submission(&host, distro(), &submission("acme", 1001, &["a.test"], 10))
        .unwrap_err();

    assert!(matches!(error, MailError::ConfigRejected { .. }));
    assert_eq!(host.file(ACCOUNTS), None);
    assert_eq!(host.file(USERS), None);
}

fn refused_with(host: &FakeMailHost) -> LocalSubmissionHazard {
    match set_local_submission(host, distro(), &submission("acme", 1001, &["a.test"], 10))
        .unwrap_err()
    {
        MailError::UnsafeLocalSubmission { hazard } => hazard,
        other => panic!("expected a hazard, got {other:?}"),
    }
}

const CUSTOMER: &str = "acme:x:1001:1001::/home/acme:/bin/bash\n";

#[test]
fn a_customer_whose_primary_group_is_postdrop_is_refused_and_nothing_is_written() {
    let host = FakeMailHost::new()
        .enabled()
        .with_passwd("acme:x:1001:120::/home/acme:/bin/bash\n");

    assert_eq!(
        refused_with(&host),
        LocalSubmissionHazard::MemberOfPrivilegedGroup {
            group: "postdrop".to_owned()
        }
    );
    assert!(host.writes().is_empty());
}

#[test]
fn a_customer_in_the_postfix_group_by_membership_is_refused() {
    let host = FakeMailHost::new()
        .enabled()
        .with_passwd(CUSTOMER)
        .with_groups("postfix:x:119:acme\npostdrop:x:120:\n");

    assert_eq!(
        refused_with(&host),
        LocalSubmissionHazard::MemberOfPrivilegedGroup {
            group: "postfix".to_owned()
        }
    );
}

#[test]
fn a_customer_in_the_postdrop_group_by_membership_is_refused() {
    let host = FakeMailHost::new()
        .enabled()
        .with_passwd(CUSTOMER)
        .with_groups("postfix:x:119:\npostdrop:x:120:other,acme\n");

    assert!(matches!(
        refused_with(&host),
        LocalSubmissionHazard::MemberOfPrivilegedGroup { .. }
    ));
}

#[test]
fn a_postdrop_that_is_not_setgid_root_or_is_missing_is_refused() {
    for facts in [
        Some(PathFacts {
            mode: 0o755,
            uid: 0,
            gid: 120,
        }),
        Some(PathFacts {
            mode: 0o2775,
            uid: 0,
            gid: 120,
        }),
        Some(PathFacts {
            mode: 0o2755,
            uid: 1001,
            gid: 120,
        }),
        Some(PathFacts {
            mode: 0o2755,
            uid: 0,
            gid: 1001,
        }),
        None,
    ] {
        let host = FakeMailHost::new()
            .enabled()
            .with_passwd(CUSTOMER)
            .with_drop_binary(facts);

        assert!(
            matches!(
                refused_with(&host),
                LocalSubmissionHazard::DropBinaryUnsafe { .. }
            ),
            "{facts:?} was accepted"
        );
        assert!(host.writes().is_empty());
    }
}

#[test]
fn a_host_without_the_drop_group_is_refused() {
    let host = FakeMailHost::new()
        .enabled()
        .with_passwd(CUSTOMER)
        .with_groups("postfix:x:119:\n");

    assert!(matches!(
        refused_with(&host),
        LocalSubmissionHazard::DropGroupUnknown { .. }
    ));
}

#[test]
fn an_ordinary_customer_on_a_sealed_host_is_bound() {
    // The accepting control of the refusals above: same customer line, groups of
    // other people only.
    let host = FakeMailHost::new()
        .enabled()
        .with_passwd(CUSTOMER)
        .with_groups("postfix:x:119:other\npostdrop:x:120:\nacme:x:1001:\n");

    set_local_submission(&host, distro(), &submission("acme", 1001, &["a.test"], 10)).unwrap();

    assert_eq!(host.file(ACCOUNTS).as_deref(), Some("1001 acme\n"));
}

#[test]
fn a_uid_the_user_database_does_not_list_is_refused_not_passed() {
    // The system user may come from a directory service the file does not show;
    // its groups cannot be judged, so it is not bound.
    let host = FakeMailHost::new()
        .enabled()
        .with_passwd("root:x:0:0::/root:/bin/bash\n");

    assert_eq!(
        refused_with(&host),
        LocalSubmissionHazard::SubmitterUnknown { uid: 1001 }
    );
    assert!(host.writes().is_empty());
}

#[test]
fn a_filter_that_does_not_report_the_rule_leaves_postdrop_closed() {
    let host = FakeMailHost::new().enabled().with_unloaded_rule();

    assert_eq!(refused_with(&host), LocalSubmissionHazard::RuleNotLoaded);
    assert_eq!(host.file(USERS), None);
}

#[test]
fn a_domain_another_account_owns_is_refused_with_a_typed_conflict() {
    let host = FakeMailHost::new()
        .enabled()
        .with_file(DOMAINS, "other a.test\n");

    let error = set_local_submission(&host, distro(), &submission("acme", 1001, &["a.test"], 10))
        .unwrap_err();

    assert!(matches!(error, MailError::LocalSubmissionConflict { .. }));
    assert_eq!(host.file(ACCOUNTS), None);
}
