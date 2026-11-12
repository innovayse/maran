//! Byte-for-byte comparison of the local-submission rule's rendered settings, and
//! checks that the shipped Lua rule carries the contract's four refusals
//! (docs/mail/local-submission.md).
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_templates::postfix::postfix_local_submitter_headers::PostfixLocalSubmitterHeaders;
use maran_templates::postfix::postfix_master_config::PostfixMasterConfig;
use maran_templates::rspamd::rspamd_dkim_signing::RspamdDkimSigning;
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
        host_mail_name: "mail.example.test".to_owned(),
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
    let master = PostfixMasterConfig {
        self_clients_path: "/etc/maran/mail/self_clients.cidr".to_owned(),
        local_submitter_headers_path: "/etc/maran/mail/local_submitter_headers.regexp".to_owned(),
    }
    .render_config()
    .unwrap();
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

/// Postfix shows a milter no Received line of its own, so the uid reaches the rule in
/// a header the `cleanup_local` service's table makes: the rule and the table must
/// spell that header the same, the table must run only on that service, and the
/// rule must no longer read the Received line (issue #591).
#[test]
fn the_rule_and_the_local_service_table_agree_on_the_submitter_header() {
    let rule = RspamdLocalSubmit::lua_rule();
    let table = PostfixLocalSubmitterHeaders::table();
    assert!(rule.contains("M.SUBMITTER_HEADER = 'X-Maran-Local-Submitter'"));
    assert!(table.contains("PREPEND X-Maran-Local-Submitter: $1 $2"));
    assert!(!rule.contains("get_header_full('Received')"));
    let master = PostfixMasterConfig {
        self_clients_path: "/etc/maran/mail/self_clients.cidr".to_owned(),
        local_submitter_headers_path: "/etc/maran/mail/local_submitter_headers.regexp".to_owned(),
    }
    .render_config()
    .unwrap();
    let local = master.split("cleanup_local unix").nth(1).unwrap();
    let local = local.split("qmgr").next().unwrap();
    assert!(local.contains("header_checks=regexp:/etc/maran/mail/local_submitter_headers.regexp"));
    assert_eq!(master.matches("header_checks=").count(), 1);
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

/// The proof key is one spelling, rendered into the settings; neither Lua file
/// spells it, and the five maps and the proof are all named by the rendered file.
#[test]
fn the_proof_key_and_the_fan_out_map_are_rendered_not_hard_coded() {
    let rendered = settings(vec![0]).render_config().unwrap();
    assert!(rendered.contains("alias_fanout_map = \"/etc/maran/mail/alias_fanout.map\";\n"));
    assert!(rendered.contains("bound_domain_key = \"maran_bound_domain\";\n"));
    assert!(!RspamdLocalSubmit::lua_rule().contains("maran_bound_domain"));
    assert!(!RspamdDkimSigning::lua_condition().contains("maran_bound_domain"));
}

/// Both roads are signed only on the proof the local-submission rule left on the
/// message: a login is not a statement about the From: header.
#[test]
fn the_dkim_condition_asks_for_the_proof_on_every_road() {
    let condition = RspamdDkimSigning::lua_condition();
    assert!(!condition.contains("return true\n  end\n  if daemon_name ~= M.LOCAL"));
    assert!(condition.contains("daemon_name ~= M.LOCAL and daemon_name ~= M.ORIGINATING"));
    let rule = RspamdLocalSubmit::lua_rule();
    assert!(rule.contains("sender not owned by account"));
    assert!(rule.contains("M.deliveries"));
}

/// The signing condition refuses by default: it signs only the two roads it
/// names, takes the road from the daemon name and never from the connection.
#[test]
fn the_dkim_condition_is_a_closed_allow_list_of_two_roads() {
    let condition = RspamdDkimSigning::lua_condition();
    assert!(condition.ends_with('\n'));
    assert!(condition.contains("MARAN_LOCAL"));
    assert!(condition.contains("ORIGINATING"));
    assert!(condition.contains("MTA-Name"));
    assert!(condition.contains("cache_get"));
    assert!(!condition.contains("get_from_ip"));
    assert!(!condition.contains("set_user"));
    assert!(!RspamdLocalSubmit::lua_rule().contains("set_user"));
}

/// Runs the Lua unit tests with the host's `lua`. Where no interpreter is on
/// PATH the test passes quietly unless `MARAN_REQUIRE_LUA=1` is set, which CI
/// sets so a missing interpreter fails the gate instead of skipping the rule's
/// tests.
#[test]
fn the_lua_unit_tests_pass() {
    let required = std::env::var("MARAN_REQUIRE_LUA").is_ok_and(|value| value == "1");
    let mut ran = false;
    for interpreter in ["lua", "lua5.4", "luajit"] {
        let mut found = true;
        for script in [
            "tests/lua/maran_local_submit_test.lua",
            "tests/lua/maran_dkim_condition_test.lua",
        ] {
            let Ok(output) = std::process::Command::new(interpreter).arg(script).output() else {
                found = false;
                break;
            };
            assert!(
                output.status.success(),
                "lua tests failed ({script}):\n{}{}",
                String::from_utf8_lossy(&output.stdout),
                String::from_utf8_lossy(&output.stderr)
            );
        }
        if found {
            ran = true;
            break;
        }
    }
    if ran {
        return;
    }
    assert!(
        !required,
        "MARAN_REQUIRE_LUA=1 but no lua interpreter is on PATH"
    );
}

/// The rule is guarded where Rspamd raises (an unknown action) and falls back to a
/// reject, and no reply text repeats the class and code Rspamd prepends itself.
#[test]
fn the_rule_guards_set_pre_result_and_carries_no_enhanced_status_codes() {
    let rule = RspamdLocalSubmit::lua_rule();
    assert!(
        rule.contains("pcall(task.set_pre_result, task, answer.action, answer.message, MODULE)")
    );
    assert!(rule.contains("task:set_pre_result('reject', answer.message, MODULE)"));
    for code in ["4.4.3", "4.7.1", "5.5.3", "5.7.1"] {
        assert!(
            !rule.contains(&format!("'{code} ")),
            "a reply text starts with {code}"
        );
    }
}

/// The condition hands the signing module a domain, a selector and a key, or false:
/// an empty table is an error in the module.
#[test]
fn the_dkim_condition_returns_parameters_or_false_never_an_empty_table() {
    let condition = RspamdDkimSigning::lua_condition();
    assert!(!condition.contains("return {}"));
    assert!(condition.contains("M.parameters("));
    assert!(condition.contains("selectors:get_key("));
    assert!(condition.contains("return false"));
}

/// Internationalised From: domains are compared as A-labels.
#[test]
fn the_rule_compares_from_domains_as_a_labels() {
    let rule = RspamdLocalSubmit::lua_rule();
    assert!(rule.contains("function M.to_ascii_domain("));
    assert!(rule.contains("function M.punycode_encode("));
    assert!(rule.contains("return M.to_ascii_domain(tostring(address.domain))"));
}
