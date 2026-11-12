//! Bringing one daemon to its rendered configuration.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::host_addresses::HostAddresses;

use crate::mail::build_mail_stages::build_mail_stages;
use crate::mail::converge_mail_stage::converge_mail_stage;
use crate::mail::fake_mail_host::{FakeMailHost, VERSION_ID, distro, hostname, settings};
use crate::mail::mail_error::MailError;
use crate::mail::mail_host::MailHost;
use crate::mail::model::mail_daemon::MailDaemon;
use crate::mail::model::mail_layout::MailLayout;
use crate::mail::model::mail_stage::MailStage;

fn postfix_stage(host: &FakeMailHost) -> MailStage {
    let layout = MailLayout::resolve(distro(), VERSION_ID).unwrap();
    let settings = settings();
    let certificate = host.certificate_state(&hostname()).unwrap();
    build_mail_stages(
        distro(),
        &layout,
        &settings,
        &certificate,
        105,
        &HostAddresses::default(),
    )
    .unwrap()
    .into_iter()
    .find(|stage| stage.daemon == MailDaemon::Postfix)
    .unwrap()
}

fn rspamd_stage(host: &FakeMailHost) -> MailStage {
    let layout = MailLayout::resolve(distro(), VERSION_ID).unwrap();
    let settings = settings();
    let certificate = host.certificate_state(&hostname()).unwrap();
    build_mail_stages(
        distro(),
        &layout,
        &settings,
        &certificate,
        105,
        &HostAddresses::default(),
    )
    .unwrap()
    .into_iter()
    .find(|stage| stage.daemon == MailDaemon::Rspamd)
    .unwrap()
}

#[test]
fn a_stage_is_unmasked_written_started_and_only_then_enabled() {
    let host = FakeMailHost::new();
    let stage = postfix_stage(&host);

    converge_mail_stage(&host, distro(), &stage).unwrap();

    assert_eq!(
        host.verbs_for("postfix"),
        ["unmask", "reload-or-restart", "is-active", "enable"]
    );
    assert!(!host.is_masked("postfix"));
    assert!(host.is_active("postfix"));
    assert!(host.is_enabled("postfix"));
    assert_eq!(
        host.file("/etc/postfix/main.cf").as_deref(),
        Some(stage.files[0].contents.as_str())
    );
}

#[test]
fn a_stage_whose_files_are_current_and_whose_unit_runs_writes_and_restarts_nothing() {
    let host = FakeMailHost::new();
    let stage = postfix_stage(&host);
    converge_mail_stage(&host, distro(), &stage).unwrap();
    let writes_before = host.writes().len();

    converge_mail_stage(&host, distro(), &stage).unwrap();

    assert_eq!(host.writes().len(), writes_before);
    assert_eq!(
        host.verbs_for("postfix")
            .iter()
            .filter(|verb| *verb == "reload-or-restart")
            .count(),
        1
    );
}

#[test]
fn current_files_do_not_count_as_converged_while_the_unit_is_stopped() {
    // The inverse control for the test above: the same bytes with nothing running
    // are what a disable leaves, and they must be started again.
    let host = FakeMailHost::new();
    let stage = postfix_stage(&host);
    let host = host
        .with_file("/etc/postfix/main.cf", &stage.files[0].contents)
        .with_file("/etc/postfix/master.cf", &stage.files[1].contents)
        .with_file(
            "/etc/maran/mail/self_clients.cidr",
            &stage.files[2].contents,
        );

    converge_mail_stage(&host, distro(), &stage).unwrap();

    assert!(host.is_active("postfix"));
}

#[test]
fn a_daemon_whose_check_refuses_leaves_the_previous_files_and_is_not_enabled() {
    let host = FakeMailHost::new().with_refusing_check(MailDaemon::Postfix, "bad parameter");
    let stage = postfix_stage(&host);
    let host = host.with_file("/etc/postfix/main.cf", "previous\n");

    let refused = converge_mail_stage(&host, distro(), &stage).unwrap_err();

    assert_eq!(
        refused,
        MailError::ConfigRejected {
            daemon: MailDaemon::Postfix,
            output: "bad parameter".to_owned()
        }
    );
    assert_eq!(
        host.file("/etc/postfix/main.cf").as_deref(),
        Some("previous\n")
    );
    assert!(!host.is_enabled("postfix"));
}

/// A Rspamd 4.x refuses to start on a condition file it cannot load, which would stop all mail
/// (Postfix then queues it). The configuration test is what keeps such a file from being live:
/// the previous condition file stays, and Rspamd is not reloaded onto the new one.
#[test]
fn rspamd_whose_configtest_refuses_keeps_the_previous_condition_file_and_is_not_reloaded() {
    let host = FakeMailHost::new().with_refusing_check(MailDaemon::Rspamd, "lua error");
    let stage = rspamd_stage(&host);
    let condition = "/etc/rspamd/lua.local.d/maran_dkim_condition.lua";
    let host = host.with_file(condition, "previous\n");

    let refused = converge_mail_stage(&host, distro(), &stage).unwrap_err();

    assert_eq!(
        refused,
        MailError::ConfigRejected {
            daemon: MailDaemon::Rspamd,
            output: "lua error".to_owned()
        }
    );
    assert_eq!(host.file(condition).as_deref(), Some("previous\n"));
    assert!(!host.is_enabled(distro().rspamd_service()));
}

/// The accepting control of the test above: with a configuration test that passes, the same
/// stage replaces the condition file, so the refusal is the check's and not the stage's.
#[test]
fn rspamd_whose_configtest_accepts_gets_the_new_condition_file() {
    let host = FakeMailHost::new();
    let stage = rspamd_stage(&host);
    let condition = "/etc/rspamd/lua.local.d/maran_dkim_condition.lua";
    let host = host.with_file(condition, "previous\n");

    converge_mail_stage(&host, distro(), &stage).unwrap();

    let expected = &stage
        .files
        .iter()
        .find(|file| file.target.ends_with("maran_dkim_condition.lua"))
        .unwrap()
        .contents;
    assert_eq!(host.file(condition).as_deref(), Some(expected.as_str()));
}

#[test]
fn a_unit_the_service_manager_refuses_to_start_is_a_refusal_naming_it() {
    let host = FakeMailHost::new().with_refusing_unit("postfix");
    let stage = postfix_stage(&host);

    let refused = converge_mail_stage(&host, distro(), &stage).unwrap_err();

    assert_eq!(
        refused,
        MailError::ServiceRefused {
            unit: "postfix".to_owned()
        }
    );
    assert!(!host.is_enabled("postfix"));
    assert_eq!(host.file("/etc/postfix/main.cf"), None);
}

#[test]
fn a_changed_address_list_makes_a_running_postfix_stage_differ_and_it_is_rewritten() {
    // Issue #531: the table is a file of the stage, so converging judges it too.
    let host = FakeMailHost::new();
    let stage = postfix_stage(&host);
    converge_mail_stage(&host, distro(), &stage).unwrap();
    let writes = host.writes().len();

    let layout = MailLayout::resolve(distro(), VERSION_ID).unwrap();
    let certificate = host.certificate_state(&hostname()).unwrap();
    let gained = HostAddresses {
        addresses: vec![
            "192.0.2.10".parse().unwrap(),
            "198.51.100.7".parse().unwrap(),
        ],
        local_prefixes: Vec::new(),
    };
    let regrown = build_mail_stages(distro(), &layout, &settings(), &certificate, 105, &gained)
        .unwrap()
        .into_iter()
        .find(|stage| stage.daemon == MailDaemon::Postfix)
        .unwrap();
    converge_mail_stage(&host, distro(), &regrown).unwrap();

    assert_eq!(host.writes().len(), writes + 1);
    assert!(
        host.file("/etc/maran/mail/self_clients.cidr")
            .unwrap()
            .contains("198.51.100.7/32")
    );
}
