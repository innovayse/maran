//! Tests for the wire code every firewall failure travels as.
//!
//! One assertion per variant of `FirewallError`, because the codes are a
//! CONTRACT — `firewall.proto` names them to callers — and because
//! `rules/testing.md` requires every typed error variant to appear in at least
//! one test.
//!
//! This area is the one that DOES fill `tool_output`, and two tests are about
//! exactly which variants may (rules/security.md item 8).

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::firewall::FirewallError;

use super::to_agent_error;
use crate::proto::ErrorCode;

/// What `nft` is imagined to have written to standard error.
const NFT_STDERR: &str = "/etc/maran/firewall.nft:12:1-9: Error: syntax error";

/// Every variant this area can produce.
fn every_variant() -> Vec<FirewallError> {
    vec![
        FirewallError::AlreadyExists,
        FirewallError::NotFound,
        FirewallError::ForeignRuleset,
        FirewallError::PortsDisagree,
        FirewallError::RuleRefusedByNft {
            stderr: NFT_STDERR.to_owned(),
        },
        FirewallError::NftFailed {
            stderr: NFT_STDERR.to_owned(),
        },
        FirewallError::UnreadableNftOutput,
        FirewallError::RulesetUnreadable,
        FirewallError::RenderFailed,
        FirewallError::StagingFailed,
    ]
}

/// The code `error` is reported as.
fn code_of(error: &FirewallError) -> i32 {
    to_agent_error(error).code
}

#[test]
fn a_rule_the_firewall_already_grants_is_an_idempotency_outcome_not_a_fault() {
    assert_eq!(
        code_of(&FirewallError::AlreadyExists),
        ErrorCode::AlreadyExists as i32
    );
}

#[test]
fn a_rule_or_ban_that_is_not_there_is_reported_as_not_found() {
    assert_eq!(
        code_of(&FirewallError::NotFound),
        ErrorCode::NotFound as i32
    );
}

#[test]
fn a_ruleset_nft_refused_is_a_validation_failure_carrying_nfts_own_message() {
    // ERROR_CODE_VALIDATION_FAILED is what rules/proto.md defines for a
    // rendered config its validator rejected. The message travels because this
    // surface is admin-only and an `nft` refusal is unintelligible without it —
    // it is the operator's only way to tell "port 0 is not a port" from "this
    // kernel has no inet family".
    let error = to_agent_error(&FirewallError::RuleRefusedByNft {
        stderr: NFT_STDERR.to_owned(),
    });

    assert_eq!(error.code, ErrorCode::ValidationFailed as i32);
    assert_eq!(error.tool_output, NFT_STDERR);
}

#[test]
fn an_nft_invocation_that_failed_is_a_system_failure_carrying_its_message() {
    let error = to_agent_error(&FirewallError::NftFailed {
        stderr: NFT_STDERR.to_owned(),
    });

    assert_eq!(error.code, ErrorCode::SystemFailure as i32);
    assert_eq!(error.tool_output, NFT_STDERR);
}

#[test]
fn a_ruleset_this_agent_did_not_write_is_a_fault_of_the_host_not_of_the_request() {
    // Deliberately not INVALID_INPUT. Nothing the panel sent can cause it — the
    // file at the ruleset path was written by somebody else, or left half
    // written by a crashed apply — so telling a customer their input was wrong
    // would send them to change something that is already correct.
    assert_eq!(
        code_of(&FirewallError::ForeignRuleset),
        ErrorCode::SystemFailure as i32
    );
}

#[test]
fn ports_that_do_not_match_the_rendered_file_take_neither_side() {
    // The split that keeps `ForeignRuleset` honest. This one is reached only
    // after the file has proved itself ours, so the disagreement is between the
    // ports the caller named and the ports the file was rendered for — and the
    // agent cannot tell which half is stale.
    //
    // VALIDATION_FAILED says exactly that: the request was well formed and the
    // agent's own check refused it after looking at the host. SYSTEM_FAILURE
    // would send an operator to inspect a ruleset that is intact, and
    // INVALID_INPUT would assert the panel is wrong when the likelier stale
    // half is the file.
    assert_eq!(
        code_of(&FirewallError::PortsDisagree),
        ErrorCode::ValidationFailed as i32
    );
    for other in [FirewallError::ForeignRuleset, FirewallError::NotFound] {
        assert_ne!(
            code_of(&FirewallError::PortsDisagree),
            code_of(&other),
            "{other:?} is a different problem and must not arrive as one code \
             with a ports disagreement"
        );
    }

    // The message is the only recovery an operator gets, since no rpc gets the
    // host out of this state. It has to name the command and the flag as they
    // actually are — `--ssh-ports` does not exist, and a message naming it
    // would send somebody to a binary that refuses it.
    let message = to_agent_error(&FirewallError::PortsDisagree).message;
    assert!(
        message.contains("maran-agent render-firewall-ruleset"),
        "the message must name the recovery: {message}"
    );
    assert!(
        message.contains("--ssh-port <port>") && message.contains("--panel-port <port>"),
        "the message must name the flags the binary really takes: {message}"
    );
    assert!(
        !message.contains("--ssh-ports"),
        "there is no --ssh-port_s_ flag; the repeatable one is --ssh-port: {message}"
    );
    assert!(
        message.contains("stale"),
        "the message must say which half is likely stale: {message}"
    );
}

#[test]
fn the_remaining_host_faults_are_system_failures() {
    for error in [
        FirewallError::UnreadableNftOutput,
        FirewallError::RulesetUnreadable,
        FirewallError::RenderFailed,
        FirewallError::StagingFailed,
    ] {
        assert_eq!(
            code_of(&error),
            ErrorCode::SystemFailure as i32,
            "{error:?}"
        );
    }
}

#[test]
fn no_variant_is_ever_reported_as_the_unspecified_code() {
    // ERROR_CODE_UNSPECIFIED means "the agent failed without classifying the
    // failure" (common.proto).
    for error in every_variant() {
        assert_ne!(
            code_of(&error),
            ErrorCode::Unspecified as i32,
            "{error:?} must be classified"
        );
    }
}

#[test]
fn only_the_two_variants_that_hold_nfts_output_put_anything_in_tool_output() {
    // The other seven have no field an output could come from, and this pins
    // that the mapping does not invent one — a refusal must not be able to echo
    // back a value the caller planted, and the only text that may travel is
    // text `nft` itself wrote.
    for error in every_variant() {
        let wire = to_agent_error(&error);
        let carries = matches!(
            error,
            FirewallError::RuleRefusedByNft { .. } | FirewallError::NftFailed { .. }
        );

        assert_eq!(
            wire.tool_output.is_empty(),
            !carries,
            "{error:?} carries the wrong tool output: {:?}",
            wire.tool_output
        );
        assert_eq!(
            wire.message,
            error.to_string(),
            "the mapping must not invent a message beside the variant's own"
        );
    }
}
