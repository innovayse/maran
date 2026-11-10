//! Byte-for-byte comparison of every rendered mail artifact against its golden.
//!
//! The golden diff is the review artifact for a template change: a reviewer reads
//! what Postfix, Dovecot, Rspamd and the key-value server will actually be told
//! (rules/testing.md).
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_templates::dovecot::dovecot_config_v23::DovecotConfigV23;
use maran_templates::dovecot::dovecot_config_v24::DovecotConfigV24;
use maran_templates::key_value::key_value_server_config::KeyValueServerConfig;
use maran_templates::postfix::postfix_main_config::PostfixMainConfig;
use maran_templates::postfix::postfix_master_config::PostfixMasterConfig;
use maran_templates::rspamd::rspamd_actions::RspamdActions;
use maran_templates::rspamd::rspamd_dkim_signing::RspamdDkimSigning;
use maran_templates::rspamd::rspamd_redis::RspamdRedis;
use maran_templates::rspamd::rspamd_worker_controller::RspamdWorkerController;
use maran_templates::rspamd::rspamd_worker_proxy::RspamdWorkerProxy;

const CERTIFICATE: &str = "/etc/maran/certificates/mail.example.test/fullchain.pem";
const KEY: &str = "/etc/maran/certificates/mail.example.test/privkey.pem";

/// Reads a golden by the name of the render type that produces it.
fn golden(relative: &str) -> String {
    std::fs::read_to_string(format!("tests/golden/{relative}"))
        .unwrap_or_else(|error| panic!("golden {relative} is missing: {error}"))
}

fn main_config() -> PostfixMainConfig {
    PostfixMainConfig {
        hostname: "mail.example.test".to_owned(),
        message_size_bytes: 52_428_800,
        recipient_limit: 100,
        map_type: "lmdb".to_owned(),
        virtual_domains_path: "/etc/maran/mail/virtual_domains".to_owned(),
        virtual_mailboxes_path: "/etc/maran/mail/virtual_mailboxes".to_owned(),
        virtual_aliases_path: "/etc/maran/mail/virtual_aliases".to_owned(),
        sender_login_maps_path: "/etc/maran/mail/sender_login_maps".to_owned(),
        local_submit_users_path: "/etc/postfix/maran_local_submit_users".to_owned(),
        lmtp_socket: "/run/dovecot/maran-lmtp".to_owned(),
        sasl_socket: "/run/dovecot/maran-auth".to_owned(),
        milter_socket: "/run/rspamd/maran-milter.sock".to_owned(),
        certificate_path: CERTIFICATE.to_owned(),
        private_key_path: KEY.to_owned(),
    }
}

fn dovecot_v23(pop3_enabled: bool) -> DovecotConfigV23 {
    DovecotConfigV23 {
        pop3_enabled,
        certificate_path: CERTIFICATE.to_owned(),
        private_key_path: KEY.to_owned(),
        users_path: "/etc/maran/mail/dovecot/users".to_owned(),
        sasl_socket: "/run/dovecot/maran-auth".to_owned(),
        lmtp_socket: "/run/dovecot/maran-lmtp".to_owned(),
        postfix_user: "postfix".to_owned(),
    }
}

fn dovecot_v24(pop3_enabled: bool) -> DovecotConfigV24 {
    DovecotConfigV24 {
        pop3_enabled,
        certificate_path: CERTIFICATE.to_owned(),
        private_key_path: KEY.to_owned(),
        users_path: "/etc/maran/mail/dovecot/users".to_owned(),
        sasl_socket: "/run/dovecot/maran-auth".to_owned(),
        lmtp_socket: "/run/dovecot/maran-lmtp".to_owned(),
        postfix_user: "postfix".to_owned(),
    }
}

#[test]
fn postfix_main_config_renders_its_golden() {
    assert_eq!(
        main_config().render_config().unwrap(),
        golden("postfix/postfix_main_config.cf")
    );
}

#[test]
fn postfix_main_config_carries_the_map_type_it_was_given_and_never_hash() {
    // The sentinel is a spelling no family uses, so the assertion can only pass if
    // the value reached the file; a literal `lmdb` in the template would not.
    let mut config = main_config();
    config.map_type = "sentinelmap".to_owned();
    let rendered = config.render_config().unwrap();

    // Five tables: the three virtual maps, the sender-login map and the local-submit users.
    assert_eq!(rendered.matches("sentinelmap:").count(), 5);
    assert!(!rendered.contains("hash:"));
}

/// The value of the last non-comment `name = value` line, the way Postfix reads it.
fn parameter(rendered: &str, name: &str) -> Option<String> {
    rendered
        .lines()
        .filter(|line| !line.starts_with('#'))
        .filter_map(|line| line.split_once('='))
        .filter(|(key, _)| key.trim() == name)
        .map(|(_, value)| value.trim().to_owned())
        .next_back()
}

#[test]
fn port_25_trusts_no_network_and_relays_for_nobody() {
    // Issue #458: loopback in mynetworks let any customer process relay to any
    // recipient with any sender, skipping the login, the sender-login check and
    // the send limit. Port 25 accepts mail for a served domain and nothing else.
    let rendered = main_config().render_config().unwrap();

    assert_eq!(parameter(&rendered, "mynetworks").as_deref(), Some(""));
    assert!(
        !rendered
            .lines()
            .filter(|line| !line.starts_with('#'))
            .any(|line| line.contains("permit_"))
    );
    assert_eq!(
        parameter(&rendered, "smtpd_relay_restrictions").as_deref(),
        Some("reject_unauth_destination")
    );
    assert_eq!(
        parameter(&rendered, "smtpd_recipient_restrictions").as_deref(),
        Some("reject_non_fqdn_recipient, reject_unauth_destination")
    );
    assert_eq!(
        parameter(&rendered, "smtpd_sasl_auth_enable").as_deref(),
        Some("no")
    );
}

#[test]
fn local_submission_is_closed_to_every_user_but_the_ones_a_table_names() {
    // Issue #500: sendmail and postdrop write into the maildrop queue and pickup
    // delivers with no smtpd restriction, so any customer script could send to
    // anyone with any sender, past the login, the sender-login check and the
    // send limit. Postfix's own gate for that road is authorized_submit_users,
    // whose default is everybody.
    let rendered = main_config().render_config().unwrap();
    let users = parameter(&rendered, "authorized_submit_users").unwrap();

    assert_eq!(
        users,
        "root, $mail_owner, lmdb:/etc/postfix/maran_local_submit_users"
    );
    assert!(!users.contains("anyone"));
    // The table is read by the customer's uid in postdrop: outside /etc/maran.
    assert!(!users.contains("/etc/maran"));
}

#[test]
fn the_local_submit_table_follows_the_path_it_is_given() {
    // The accepting control of the test above: the line is built from the field,
    // not a literal, so a family with another directory gets its own path.
    let mut config = main_config();
    config.local_submit_users_path = "/usr/local/etc/postfix/maran_local_submit_users".to_owned();
    config.map_type = "hash".to_owned();
    let rendered = config.render_config().unwrap();

    assert_eq!(
        parameter(&rendered, "authorized_submit_users").as_deref(),
        Some("root, $mail_owner, hash:/usr/local/etc/postfix/maran_local_submit_users")
    );
}

#[test]
fn the_port_25_service_overrides_nothing_so_it_keeps_the_strict_defaults() {
    // The accepting side of the control below: `smtp inet` must stay a bare line,
    // because an `-o` under it could re-open what main.cf closed.
    let rendered = PostfixMasterConfig {}.render_config().unwrap();
    let mut lines = rendered
        .lines()
        .skip_while(|line| !line.starts_with("smtp       inet"));
    assert!(lines.next().is_some());
    assert!(!lines.next().is_some_and(|line| line.starts_with(' ')));
}

#[test]
fn the_submission_services_take_a_login_and_bind_the_sender_to_it() {
    // Inverse control of the refusals above: the one way to relay is still open,
    // behind SASL and the sender-login check, on both submission services.
    let rendered = PostfixMasterConfig {}.render_config().unwrap();
    for service in ["submission inet", "smtps      inet"] {
        let block: Vec<&str> = rendered
            .lines()
            .skip_while(|line| !line.starts_with(service))
            .skip(1)
            .take_while(|line| line.starts_with(' '))
            .collect();
        for required in [
            "-o smtpd_sasl_auth_enable=yes",
            "-o smtpd_sender_restrictions=reject_sender_login_mismatch",
            "-o smtpd_relay_restrictions=permit_sasl_authenticated,reject",
            "-o smtpd_recipient_restrictions=reject_non_fqdn_recipient,permit_sasl_authenticated,reject",
        ] {
            assert!(
                block.iter().any(|line| line.trim() == required),
                "{service} lacks {required}"
            );
        }
        assert!(!block.iter().any(|line| line.contains("permit_mynetworks")));
    }
}

#[test]
fn postfix_master_config_renders_its_golden() {
    assert_eq!(
        PostfixMasterConfig {}.render_config().unwrap(),
        golden("postfix/postfix_master_config.cf")
    );
}

#[test]
fn no_postfix_service_is_chrooted() {
    // The sockets to Dovecot and Rspamd are outside the queue directory, so a
    // chrooted service could not reach them.
    let rendered = PostfixMasterConfig {}.render_config().unwrap();
    for line in rendered
        .lines()
        .filter(|line| !line.starts_with('#') && !line.starts_with(' ') && !line.trim().is_empty())
    {
        let columns: Vec<&str> = line.split_whitespace().collect();
        assert_eq!(columns.get(4), Some(&"n"), "chrooted service: {line}");
    }
}

#[test]
fn dovecot_23_renders_its_goldens() {
    assert_eq!(
        dovecot_v23(false).render_config().unwrap(),
        golden("dovecot/dovecot_config_v23.conf")
    );
    assert_eq!(
        dovecot_v23(true).render_config().unwrap(),
        golden("dovecot/dovecot_config_v23_pop3.conf")
    );
}

#[test]
fn dovecot_24_renders_its_goldens() {
    assert_eq!(
        dovecot_v24(false).render_config().unwrap(),
        golden("dovecot/dovecot_config_v24.conf")
    );
    assert_eq!(
        dovecot_v24(true).render_config().unwrap(),
        golden("dovecot/dovecot_config_v24_pop3.conf")
    );
}

#[test]
fn pop3_is_served_only_when_asked_for() {
    assert!(
        dovecot_v23(true)
            .render_config()
            .unwrap()
            .contains("protocols = imap pop3 lmtp\n")
    );
    assert!(
        dovecot_v23(false)
            .render_config()
            .unwrap()
            .contains("protocols = imap lmtp\n")
    );
    assert!(
        dovecot_v24(true)
            .render_config()
            .unwrap()
            .contains("protocols = imap pop3 lmtp\n")
    );
    assert!(
        dovecot_v24(false)
            .render_config()
            .unwrap()
            .contains("protocols = imap lmtp\n")
    );
}

#[test]
fn the_two_dovecot_generations_open_with_their_own_spelling() {
    // 2.4 refuses a file without its version line and 2.3 refuses that line, so
    // a render for the wrong generation is a daemon that does not start.
    assert!(
        dovecot_v24(false)
            .render_config()
            .unwrap()
            .contains("dovecot_config_version = 2.4.0\n")
    );
    assert!(
        !dovecot_v23(false)
            .render_config()
            .unwrap()
            .contains("dovecot_config_version")
    );
}

#[test]
fn rspamd_files_render_their_goldens() {
    assert_eq!(
        RspamdWorkerProxy {
            socket_path: "/run/rspamd/maran-milter.sock".to_owned(),
            postfix_group: "postfix".to_owned(),
        }
        .render_config()
        .unwrap(),
        golden("rspamd/rspamd_worker_proxy.inc")
    );
    assert_eq!(
        RspamdWorkerController {
            socket_path: "/run/rspamd/maran-controller.sock".to_owned(),
        }
        .render_config()
        .unwrap(),
        golden("rspamd/rspamd_worker_controller.inc")
    );
    assert_eq!(
        RspamdRedis {
            socket_path: "/run/redis/redis-server.sock".to_owned(),
        }
        .render_config()
        .unwrap(),
        golden("rspamd/rspamd_redis.conf")
    );
    assert_eq!(
        RspamdActions {
            greylist: 4,
            add_header: 6,
            reject: 15,
        }
        .render_config()
        .unwrap(),
        golden("rspamd/rspamd_actions.conf")
    );
    assert_eq!(
        RspamdDkimSigning {
            selectors_path: "/etc/maran/mail/dkim/selectors".to_owned(),
            keys_directory: "/etc/maran/mail/dkim".to_owned(),
        }
        .render_config()
        .unwrap(),
        golden("rspamd/rspamd_dkim_signing.conf")
    );
}

#[test]
fn the_key_value_server_config_renders_its_golden() {
    assert_eq!(
        KeyValueServerConfig {
            socket_path: "/run/redis/redis-server.sock".to_owned(),
        }
        .render_config()
        .unwrap(),
        golden("key_value/key_value_server_config.conf")
    );
}

#[test]
fn the_key_value_server_has_no_tcp_listener() {
    let rendered = KeyValueServerConfig {
        socket_path: "/run/redis/redis-server.sock".to_owned(),
    }
    .render_config()
    .unwrap();

    assert!(rendered.lines().any(|line| line == "port 0"));
}

#[test]
fn a_line_appended_to_a_rendered_mail_file_is_a_line_of_its_own() {
    // askama drops one trailing newline, so each template ends with a comment that
    // keeps it; without it an appended parameter fuses with the last one.
    let renders = [
        main_config().render_config().unwrap(),
        PostfixMasterConfig {}.render_config().unwrap(),
        dovecot_v23(false).render_config().unwrap(),
        dovecot_v24(false).render_config().unwrap(),
        KeyValueServerConfig {
            socket_path: "/run/redis/redis-server.sock".to_owned(),
        }
        .render_config()
        .unwrap(),
    ];

    for rendered in renders {
        assert!(rendered.ends_with('\n'));
        assert!(!rendered.ends_with("\n\n"));
    }
}
