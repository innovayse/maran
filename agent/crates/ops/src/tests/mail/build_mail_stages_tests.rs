//! The plan the whole mail configuration is rendered into.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{DistroFamily, adapter_for};

use crate::mail::build_mail_stages::build_mail_stages;
use crate::mail::fake_mail_host::{
    FakeMailHost, VERSION_ID, distro, hostname, settings, settings_with_pop3,
};
use crate::mail::mail_host::MailHost;
use crate::mail::model::mail_daemon::MailDaemon;
use crate::mail::model::mail_layout::MailLayout;
use crate::mail::model::mail_stage::MailStage;

fn stages_for(family: DistroFamily, version_id: &str, pop3: bool) -> Vec<MailStage> {
    let adapter = adapter_for(family);
    let layout = MailLayout::resolve(adapter, version_id).unwrap();
    let settings = if pop3 {
        settings_with_pop3()
    } else {
        settings()
    };
    let certificate = FakeMailHost::new().certificate_state(&hostname()).unwrap();
    build_mail_stages(adapter, &layout, &settings, &certificate, 105).unwrap()
}

fn stage_of(stages: &[MailStage], daemon: MailDaemon) -> &MailStage {
    stages.iter().find(|stage| stage.daemon == daemon).unwrap()
}

fn text_of<'a>(stage: &'a MailStage, file_name: &str) -> &'a str {
    &stage
        .files
        .iter()
        .find(|file| file.target.ends_with(file_name))
        .unwrap()
        .contents
}

#[test]
fn the_stages_come_in_start_order() {
    let stages = stages_for(DistroFamily::Debian, VERSION_ID, false);
    let order: Vec<MailDaemon> = stages.iter().map(|stage| stage.daemon).collect();

    assert_eq!(order, MailDaemon::START_ORDER);
}

#[test]
fn every_file_is_world_readable_and_never_writable_by_others() {
    for stage in stages_for(DistroFamily::Debian, VERSION_ID, false) {
        for file in &stage.files {
            assert_eq!(file.mode, 0o644, "{}", file.target.display());
        }
    }
}

#[test]
fn postfix_gets_its_two_files_checked_by_postfix_check() {
    let stages = stages_for(DistroFamily::Debian, VERSION_ID, false);
    let postfix = stage_of(&stages, MailDaemon::Postfix);

    let targets: Vec<_> = postfix
        .files
        .iter()
        .map(|file| file.target.to_string_lossy().into_owned())
        .collect();
    assert_eq!(targets, ["/etc/postfix/main.cf", "/etc/postfix/master.cf"]);
    assert_eq!(
        postfix.validator_program.as_str(),
        distro().postfix_binary().as_str()
    );
    assert_eq!(postfix.validator_arguments, ["check"]);
    assert_eq!(postfix.validator_class, CallClass::Validate);
}

#[test]
fn the_postfix_maps_are_written_in_the_type_the_host_names() {
    let stages = stages_for(DistroFamily::Rhel, "10.0", false);
    let main = text_of(stage_of(&stages, MailDaemon::Postfix), "main.cf");

    assert!(main.contains("virtual_mailbox_maps = lmdb:/etc/maran/mail/virtual_mailboxes\n"));
    assert!(!main.contains("hash:"));
}

#[test]
fn local_submission_is_limited_to_the_users_of_a_table_beside_main_cf() {
    // Issue #500: sendmail and postdrop bypass every smtpd restriction, so who
    // may use them is decided by a table postdrop reads as the customer's uid.
    // Both families name it in their own Postfix directory, never under
    // /etc/maran, and the line lists root and the Postfix owner explicitly.
    for (family, version_id) in [
        (DistroFamily::Debian, VERSION_ID),
        (DistroFamily::Rhel, "10.0"),
    ] {
        let stages = stages_for(family, version_id, false);
        let main = text_of(stage_of(&stages, MailDaemon::Postfix), "main.cf");

        assert!(
            main.contains(
                "authorized_submit_users = root, $mail_owner, lmdb:/etc/postfix/maran_local_submit_users\n"
            ),
            "{family:?}"
        );
        assert!(!main.contains("static:anyone"));
    }
}

#[test]
fn the_hostname_and_the_message_size_reach_postfix() {
    let stages = stages_for(DistroFamily::Debian, VERSION_ID, false);
    let main = text_of(stage_of(&stages, MailDaemon::Postfix), "main.cf");

    assert!(main.contains("myhostname = mail.example.test\n"));
    assert!(main.contains("message_size_limit = 52428800\n"));
}

#[test]
fn dovecot_2_3_and_2_4_are_two_renders_and_the_release_picks_one() {
    let on_2_3 = stages_for(DistroFamily::Debian, "12", false);
    let on_2_4 = stages_for(DistroFamily::Debian, "13", false);

    let old = text_of(stage_of(&on_2_3, MailDaemon::Dovecot), "dovecot.conf");
    let new = text_of(stage_of(&on_2_4, MailDaemon::Dovecot), "dovecot.conf");
    assert!(!old.contains("dovecot_config_version"));
    assert!(new.contains("dovecot_config_version = 2.4.0\n"));
}

#[test]
fn pop3_reaches_dovecot_only_when_enabled() {
    let off = stages_for(DistroFamily::Debian, VERSION_ID, false);
    let on = stages_for(DistroFamily::Debian, VERSION_ID, true);

    assert!(
        text_of(stage_of(&off, MailDaemon::Dovecot), "dovecot.conf")
            .contains("protocols = imap lmtp\n")
    );
    assert!(
        text_of(stage_of(&on, MailDaemon::Dovecot), "dovecot.conf")
            .contains("protocols = imap pop3 lmtp\n")
    );
}

#[test]
fn dovecot_is_checked_by_doveconf_and_rspamd_by_its_configtest() {
    let stages = stages_for(DistroFamily::Debian, VERSION_ID, false);

    let dovecot = stage_of(&stages, MailDaemon::Dovecot);
    assert_eq!(
        dovecot.validator_program.as_str(),
        distro().doveconf_binary().as_str()
    );
    assert_eq!(dovecot.validator_arguments, ["-n"]);

    let rspamd = stage_of(&stages, MailDaemon::Rspamd);
    assert_eq!(
        rspamd.validator_program.as_str(),
        distro().rspamadm_binary().as_str()
    );
    assert_eq!(rspamd.validator_arguments, ["configtest"]);
}

#[test]
fn rspamd_gets_five_additions_and_two_overrides() {
    let stages = stages_for(DistroFamily::Debian, VERSION_ID, false);
    let rspamd = stage_of(&stages, MailDaemon::Rspamd);

    let targets: Vec<_> = rspamd
        .files
        .iter()
        .map(|file| file.target.to_string_lossy().into_owned())
        .collect();
    assert_eq!(
        targets,
        [
            "/etc/rspamd/local.d/redis.conf",
            "/etc/rspamd/local.d/actions.conf",
            "/etc/rspamd/local.d/dkim_signing.conf",
            "/etc/rspamd/local.d/maran_local_submit.conf",
            "/etc/rspamd/lua.local.d/maran_local_submit.lua",
            "/etc/rspamd/override.d/worker-proxy.inc",
            "/etc/rspamd/override.d/worker-controller.inc",
        ]
    );
    assert!(text_of(rspamd, "actions.conf").contains("reject = 15;\n"));
}

#[test]
fn rspamd_and_the_key_value_server_share_one_socket() {
    let stages = stages_for(DistroFamily::Debian, VERSION_ID, false);

    let rspamd = text_of(stage_of(&stages, MailDaemon::Rspamd), "redis.conf");
    let server = text_of(stage_of(&stages, MailDaemon::KeyValueServer), "redis.conf");
    assert!(rspamd.contains("servers = \"/run/redis/redis-server.sock\";\n"));
    assert!(server.contains("unixsocket /run/redis/redis-server.sock\n"));
}

#[test]
fn the_key_value_server_is_judged_by_a_restart_because_it_has_no_check() {
    let stages = stages_for(DistroFamily::Debian, VERSION_ID, false);
    let server = stage_of(&stages, MailDaemon::KeyValueServer);

    assert_eq!(
        server.validator_arguments,
        ["reload-or-restart", "--", "redis-server"]
    );
    assert_eq!(server.reload_arguments, ["is-active", "--", "redis-server"]);
    assert_eq!(server.validator_class, CallClass::Reload);
}

#[test]
fn valkey_is_configured_where_the_adapter_says_on_release_10() {
    let stages = stages_for(DistroFamily::Rhel, "10.0", false);
    let server = stage_of(&stages, MailDaemon::KeyValueServer);

    assert_eq!(server.unit, "valkey");
    assert_eq!(
        server.files[0].target.to_string_lossy(),
        "/etc/valkey/valkey.conf"
    );
}

#[test]
fn the_local_submission_rule_exempts_root_and_the_postfix_uid() {
    let stages = stages_for(DistroFamily::Debian, VERSION_ID, false);
    let rspamd = stage_of(&stages, MailDaemon::Rspamd);
    let settings = rspamd
        .files
        .iter()
        .find(|file| file.target.ends_with("local.d/maran_local_submit.conf"))
        .unwrap();
    assert!(settings.contents.contains("exempt_uids = [0, 105];"));
    assert!(settings.contents.contains("max_recipients = 100;"));
}
