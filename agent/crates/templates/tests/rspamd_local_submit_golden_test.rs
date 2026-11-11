//! Byte-for-byte comparison of the local-submission rule's rendered settings, and
//! checks that the shipped Lua rule carries the contract's four refusals
//! (docs/mail/local-submission.md).
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_templates::postfix::postfix_master_config::PostfixMasterConfig;
use maran_templates::rspamd::rspamd_local_submit::RspamdLocalSubmit;

fn golden(relative: &str) -> String {
    std::fs::read_to_string(format!("tests/golden/{relative}"))
        .unwrap_or_else(|error| panic!("golden {relative} is missing: {error}"))
}

fn settings(exempt_uids: Vec<u32>) -> RspamdLocalSubmit {
    RspamdLocalSubmit {
        maps_directory: "/etc/maran/mail".to_owned(),
        max_recipients: 100,
        exempt_uids,
    }
}

#[test]
fn the_local_submit_settings_render_their_golden() {
    assert_eq!(
        settings(vec![0]).render_config().unwrap(),
        golden("rspamd/rspamd_local_submit.conf")
    );
}

#[test]
fn several_exempt_uids_are_comma_separated_and_none_renders_an_empty_list() {
    let several = settings(vec![0, 105]).render_config().unwrap();
    assert!(several.contains("exempt_uids = [0, 105];\n"));
    let none = settings(Vec::new()).render_config().unwrap();
    assert!(none.contains("exempt_uids = [];\n"));
}

#[test]
fn the_recipient_cap_follows_the_value_it_is_given() {
    let mut rendered = settings(vec![0]);
    rendered.max_recipients = 25;
    assert!(
        rendered
            .render_config()
            .unwrap()
            .contains("max_recipients = 25;\n")
    );
}

#[test]
fn the_shipped_lua_rule_refuses_unowned_senders_and_fails_closed() {
    let rule = RspamdLocalSubmit::lua_rule();
    assert!(rule.ends_with('\n'));
    assert!(rule.contains("sender not owned by account"));
    assert!(rule.contains("maran:send:"));
    assert!(rule.contains("soft reject"));
}

/// The road is told by the daemon name Postfix reports for the service only
/// `pickup` uses, and by nothing the message or the connection says about itself:
/// the rule and master.cf must spell the same names, and the rule must not look at
/// a client address (Postfix presents local mail as a connection from 127.0.0.1).
#[test]
fn the_rule_and_master_cf_agree_on_the_daemon_names() {
    let rule = RspamdLocalSubmit::lua_rule();
    let master = PostfixMasterConfig {}.render_config().unwrap();
    for name in ["MARAN_LOCAL", "INBOUND", "ORIGINATING"] {
        assert!(rule.contains(name), "the rule does not know {name}");
        assert!(
            master.contains(&format!("milter_macro_daemon_name={name}")),
            "master.cf does not set {name}"
        );
    }
    assert!(!rule.contains("get_from_ip"));
    assert!(!rule.contains("has_client_ip"));
}

/// A Lua error in the prefilter must refuse the mail, not let it through; the
/// verdict must be idempotent per message identity (the maildrop id of the pickup
/// header, not the cleanup queue id, which changes on every re-offer) and counted
/// per recipient.
#[test]
fn the_rule_fails_closed_counts_recipients_and_dedupes_by_message_identity() {
    let rule = RspamdLocalSubmit::lua_rule();
    assert!(rule.contains("pcall(check, task)"));
    assert!(!rule.contains("get_queue_id"));
    assert!(rule.contains("M.seen_key(decision.account, decision.identity)"));
    assert!(rule.contains("INCRBY"));
    assert!(rule.contains("'NX'"));
    assert!(rule.contains("priority = 100"));
}

/// A settings section that is missing or incomplete must register a stand-in that
/// refuses the local road, never skip the registration (a rule that is not
/// registered examines nothing and the local road would be accepted unexamined).
#[test]
fn a_missing_settings_section_registers_a_refusing_stand_in() {
    let rule = RspamdLocalSubmit::lua_rule();
    assert!(rule.contains("decide_unconfigured"));
    assert!(rule.contains("settings section maran_local_submit not found"));
    // Registration is one function used by both the rule and the stand-in.
    assert_eq!(rule.matches("rspamd_config:register_symbol").count(), 1);
    assert!(rule.contains("register(guarded)"));
    // The only early return before registration is the explicit `enabled = false`.
    assert!(rule.contains("opts.enabled == false"));
}

/// Runs the Lua unit tests with the host's `lua`. Where no interpreter is on
/// PATH the test passes quietly unless `MARAN_REQUIRE_LUA=1` is set, which CI
/// sets so a missing interpreter fails the gate instead of skipping the rule's
/// tests.
#[test]
fn the_lua_unit_tests_pass() {
    let required = std::env::var("MARAN_REQUIRE_LUA").is_ok_and(|value| value == "1");
    for interpreter in ["lua", "lua5.4", "luajit"] {
        let Ok(output) = std::process::Command::new(interpreter)
            .arg("tests/lua/maran_local_submit_test.lua")
            .output()
        else {
            continue;
        };
        assert!(
            output.status.success(),
            "lua tests failed:\n{}{}",
            String::from_utf8_lossy(&output.stdout),
            String::from_utf8_lossy(&output.stderr)
        );
        return;
    }
    assert!(
        !required,
        "MARAN_REQUIRE_LUA=1 but no lua interpreter is on PATH"
    );
}
