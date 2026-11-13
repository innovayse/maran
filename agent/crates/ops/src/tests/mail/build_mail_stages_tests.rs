//! The plan the whole mail configuration is rendered into.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::host_addresses::HostAddresses;
use maran_distro::{DistroFamily, adapter_for};

use crate::mail::build_mail_stages::build_mail_stages;
use crate::mail::fake_mail_host::{
    DEFAULT_HOST_ADDRESSES, FakeMailHost, VERSION_ID, distro, hostname, settings,
    settings_with_pop3,
};
use crate::mail::mail_host::MailHost;
use crate::mail::model::mail_daemon::MailDaemon;
use crate::mail::model::mail_layout::MailLayout;
use crate::mail::model::mail_stage::MailStage;

fn host_addresses() -> HostAddresses {
    HostAddresses {
        addresses: DEFAULT_HOST_ADDRESSES
            .iter()
            .map(|address| address.parse().unwrap())
            .collect(),
        local_prefixes: Vec::new(),
    }
}

fn stages_for(family: DistroFamily, version_id: &str, pop3: bool) -> Vec<MailStage> {
    let adapter = adapter_for(family);
    let layout = MailLayout::resolve(adapter, version_id).unwrap();
    let settings = if pop3 {
        settings_with_pop3()
    } else {
        settings()
    };
    let certificate = FakeMailHost::new().certificate_state(&hostname()).unwrap();
    build_mail_stages(
        adapter,
        &layout,
        &settings,
        &certificate,
        105,
        &host_addresses(),
    )
    .unwrap()
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
fn postfix_gets_its_three_files_checked_by_postfix_check() {
    let stages = stages_for(DistroFamily::Debian, VERSION_ID, false);
    let postfix = stage_of(&stages, MailDaemon::Postfix);

    let targets: Vec<_> = postfix
        .files
        .iter()
        .map(|file| file.target.to_string_lossy().into_owned())
        .collect();
    assert_eq!(
        targets,
        [
            "/etc/postfix/main.cf",
            "/etc/postfix/master.cf",
            "/etc/maran/mail/self_clients.cidr",
            "/etc/maran/mail/local_submitter_headers.regexp"
        ]
    );
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
fn rspamd_gets_its_additions_the_rule_as_a_module_and_three_overrides() {
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
            "/etc/rspamd/plugins.d/maran_local_submit.lua",
            "/etc/rspamd/rspamd.conf.local",
            "/etc/rspamd/lua.local.d/maran_dkim_condition.lua",
            "/etc/rspamd/override.d/worker-proxy.inc",
            "/etc/rspamd/override.d/worker-controller.inc",
            "/etc/rspamd/override.d/worker-normal.inc",
        ]
    );
    // The packaged normal worker listens on a loopback port that takes the sending
    // road from a request header; only the override switches it off.
    assert!(text_of(rspamd, "worker-normal.inc").contains("enabled = false;\n"));
    assert!(text_of(rspamd, "actions.conf").contains("reject = 15;\n"));
    // The rule is only ever run if Rspamd loads it: as a module under plugins.d with its
    // settings included from rspamd.conf.local (Rspamd reads neither lua.local.d nor an
    // unknown local.d file). The condition is NOT a module and stays out of plugins.d.
    assert!(text_of(rspamd, "rspamd.conf.local").contains(
        "maran_local_submit {\n  .include \"$LOCAL_CONFDIR/local.d/maran_local_submit.conf\"\n}\n"
    ));
    assert!(
        !targets
            .iter()
            .any(|target| target.contains("plugins.d/maran_dkim_condition"))
    );
    assert!(
        !targets
            .iter()
            .any(|target| target.contains("lua.local.d/maran_local_submit"))
    );
    // `discard` is no built-in action; the rule asks for it.
    assert!(
        text_of(rspamd, "actions.conf").contains("discard {\n  flags = [\"no_threshold\"];\n}\n")
    );
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
    assert!(
        settings
            .contents
            .contains("alias_fanout_map = \"/etc/maran/mail/alias_fanout.map\";")
    );
}

#[test]
fn dkim_signing_names_the_condition_the_stage_installs() {
    let stages = stages_for(DistroFamily::Debian, VERSION_ID, false);
    let rspamd = stage_of(&stages, MailDaemon::Rspamd);
    let signing = rspamd
        .files
        .iter()
        .find(|file| file.target.ends_with("local.d/dkim_signing.conf"))
        .unwrap();
    let condition = rspamd
        .files
        .iter()
        .find(|file| {
            file.target
                .ends_with("lua.local.d/maran_dkim_condition.lua")
        })
        .expect("the condition file is part of the stage");
    assert!(signing.contents.contains(&format!(
        "sign_condition = \"return dofile(\\\"{}\\\")(...)\";",
        condition.target.display()
    )));
}

#[test]
fn postfix_main_cf_bounds_alias_expansion() {
    let stages = stages_for(DistroFamily::Debian, VERSION_ID, false);
    let postfix = stage_of(&stages, MailDaemon::Postfix);
    let main = postfix
        .files
        .iter()
        .find(|file| file.target.ends_with("main.cf"))
        .unwrap();
    assert!(
        main.contents
            .contains("virtual_alias_expansion_limit = 100\n")
    );
}

#[test]
fn the_local_service_names_the_submitter_table_the_stage_writes() {
    // Issue #591: master.cf points at the very file that is in the same change, and
    // that file is the one that makes the header the Rspamd rule reads.
    let stages = stages_for(DistroFamily::Debian, VERSION_ID, false);
    let postfix = stage_of(&stages, MailDaemon::Postfix);
    let master = text_of(postfix, "master.cf");
    let table = text_of(postfix, "local_submitter_headers.regexp");

    assert!(
        master.contains("header_checks=regexp:/etc/maran/mail/local_submitter_headers.regexp\n")
    );
    assert!(table.contains("PREPEND X-Maran-Local-Submitter: $1"));
}

#[test]
fn the_port_25_service_names_the_table_the_stage_writes_with_the_hosts_addresses() {
    // Issue #531: master.cf points at the very file that is in the same change,
    // and that file lists the loopback and the addresses the host was asked for.
    let stages = stages_for(DistroFamily::Debian, VERSION_ID, false);
    let postfix = stage_of(&stages, MailDaemon::Postfix);
    let master = text_of(postfix, "master.cf");
    let table = text_of(postfix, "self_clients.cidr");

    assert!(master.contains("cidr:/etc/maran/mail/self_clients.cidr,permit\n"));
    for refused in [
        "127.0.0.0/8 ",
        "::1/128 ",
        "192.0.2.10/32 ",
        "2001:db8::5/128 ",
    ] {
        assert!(
            table.lines().any(|line| line.starts_with(refused)),
            "{refused}"
        );
    }
}

#[test]
fn an_address_the_host_does_not_carry_is_not_in_the_stage() {
    // Inverse control: the table is built from the addresses given, not from a
    // fixed list, so a client at any other address passes the restriction.
    let stages = stages_for(DistroFamily::Rhel, "10.0", false);
    let table = text_of(stage_of(&stages, MailDaemon::Postfix), "self_clients.cidr");

    assert!(!table.contains("198.51.100.7"));
    assert!(!table.contains("192.0.2.11"));
}

#[test]
fn every_family_writes_the_table_at_the_same_agent_owned_path() {
    for (family, version_id) in [
        (DistroFamily::Debian, VERSION_ID),
        (DistroFamily::Rhel, "10.0"),
    ] {
        let stages = stages_for(family, version_id, false);
        let postfix = stage_of(&stages, MailDaemon::Postfix);
        assert!(
            postfix
                .files
                .iter()
                .any(|file| file.target.to_string_lossy() == "/etc/maran/mail/self_clients.cidr"),
            "{family:?}"
        );
    }
}
