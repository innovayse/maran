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

    assert_eq!(rendered.matches("sentinelmap:").count(), 4);
    assert!(!rendered.contains("hash:"));
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
