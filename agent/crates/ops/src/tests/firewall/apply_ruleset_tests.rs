//! The order the apply engine takes its steps in, and what each refusal
//! leaves behind.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::agent_paths::AgentPaths;

use crate::firewall::apply_ruleset::apply_ruleset;
use crate::firewall::fake_firewall_host::{
    COMMIT_STEP, DISCARD_STEP, FakeFirewallHost, STAGE_STEP, SYNC_DIRECTORY_STEP, SYNC_FILE_STEP,
    distro, rendered, ruleset_path, staged_path,
};
use crate::firewall::firewall_error::FirewallError;

/// The step log entry for the `nft --check` of the staged file.
fn check_step() -> String {
    format!(
        "run {} --check -f {}",
        distro().nft_binary(),
        staged_path(AgentPaths::nftables_ruleset_path())
    )
}

/// The step log entry for the `nft -f` of the committed file.
fn load_step() -> String {
    format!("run {} -f {}", distro().nft_binary(), ruleset_path())
}

/// The apply checks the staged file, flushes it, renames it, flushes the
/// directory, and only then loads it — in that order and no other.
///
/// The order IS the safety property. Checking before the rename is what
/// leaves the live ruleset untouched when `nft` refuses; flushing the FILE
/// before the rename is what stops a crash from committing a directory entry
/// that points at data which never reached the disk; flushing the DIRECTORY
/// after it is what makes the rename itself durable; loading last is what
/// makes the running firewall and the file a reboot re-reads the same thing.
/// A single `write_file` host method would have made all of it invisible to
/// every test.
#[test]
fn the_apply_order_is_check_then_rename_then_load() {
    let host = FakeFirewallHost::new();

    apply_ruleset(
        &host,
        distro(),
        AgentPaths::nftables_ruleset_path(),
        &rendered(&[]),
    )
    .expect("applied");

    assert_eq!(
        host.steps(),
        vec![
            String::from(STAGE_STEP),
            check_step(),
            String::from(SYNC_FILE_STEP),
            String::from(COMMIT_STEP),
            String::from(SYNC_DIRECTORY_STEP),
            load_step(),
        ]
    );
}

/// The directory flush happens AFTER the rename, and the file's flush before
/// it.
///
/// The two flushes do different jobs and only one order works: the file's
/// makes the new contents durable and must precede the rename, the
/// directory's makes the directory ENTRY durable and does nothing at all
/// before it. Flushing the directory too early leaves a window in which an
/// unclean shutdown resolves the path back to the old inode, so
/// `nftables.service` re-reads the previous ruleset at boot and a `deny_port`
/// that reported success silently re-opens its port.
///
/// **What this test can and cannot show.** A unit test cannot cut the power,
/// so it cannot observe durability itself; what it pins is the one thing that
/// decides it and that a reader can get wrong — the position of each flush
/// relative to the rename in the recorded sequence. That is deliberately a
/// weaker claim than "the rename is durable", and it is the strongest claim
/// available without a crashing kernel. The order was previously
/// file-then-directory-then-rename, and deleting the directory flush outright
/// left the whole suite green: this test and the log assertion above are what
/// stops that from being true again.
#[test]
fn the_directory_flush_follows_the_rename_and_the_file_flush_precedes_it() {
    let host = FakeFirewallHost::new();

    apply_ruleset(
        &host,
        distro(),
        AgentPaths::nftables_ruleset_path(),
        &rendered(&[]),
    )
    .expect("applied");

    let steps = host.steps();
    let at = |wanted: &str| {
        steps
            .iter()
            .position(|step| step == wanted)
            .unwrap_or_else(|| panic!("{wanted} never happened: {steps:?}"))
    };

    assert!(
        at(SYNC_FILE_STEP) < at(COMMIT_STEP),
        "the file must be flushed before the rename: {steps:?}"
    );
    assert!(
        at(COMMIT_STEP) < at(SYNC_DIRECTORY_STEP),
        "the directory must be flushed after the rename, or the rename is not durable: {steps:?}"
    );
}

/// A ruleset `nft --check` refuses is never renamed, never loaded, and the
/// file that was there is still there.
#[test]
fn a_ruleset_nft_refuses_is_never_committed() {
    let host = FakeFirewallHost::new();
    host.put_file(&ruleset_path(), "the previous ruleset");
    host.refuse_check_with("Error: syntax error, unexpected junk");

    let outcome = apply_ruleset(
        &host,
        distro(),
        AgentPaths::nftables_ruleset_path(),
        &rendered(&[]),
    );

    assert_eq!(
        outcome,
        Err(FirewallError::RuleRefusedByNft {
            stderr: String::from("Error: syntax error, unexpected junk"),
        })
    );
    assert_eq!(
        host.file(&ruleset_path()).as_deref(),
        Some("the previous ruleset")
    );
    assert!(host.applies().is_empty(), "nothing may be loaded");
    assert!(
        host.steps().contains(&String::from(DISCARD_STEP)),
        "the staged file must be taken away again: {:?}",
        host.steps()
    );
    assert!(
        host.file(&staged_path(AgentPaths::nftables_ruleset_path()))
            .is_none()
    );
}

/// A file that will not stage never reaches `nft` at all.
#[test]
fn a_ruleset_that_cannot_be_staged_is_never_checked() {
    let host = FakeFirewallHost::new();
    host.refuse_writes();

    let outcome = apply_ruleset(
        &host,
        distro(),
        AgentPaths::nftables_ruleset_path(),
        &rendered(&[]),
    );

    assert_eq!(outcome, Err(FirewallError::StagingFailed));
    assert!(host.steps().is_empty());
}

/// A load that fails after a successful check is reported with `nft`'s own
/// answer, and the committed file stays — a retry of the same operation
/// converges rather than needing a cleanup.
#[test]
fn a_load_that_fails_after_a_successful_check_is_reported() {
    let host = FakeFirewallHost::new();
    host.refuse_load();

    let outcome = apply_ruleset(
        &host,
        distro(),
        AgentPaths::nftables_ruleset_path(),
        &rendered(&[]),
    );

    let expected = rendered(&[]);
    assert_eq!(
        outcome,
        Err(FirewallError::NftFailed {
            stderr: String::new()
        })
    );
    assert_eq!(
        host.file(&ruleset_path()).as_deref(),
        Some(expected.as_str())
    );
}

/// An `nft` that cannot be started at all is a failure, not a refusal — and
/// nothing is committed on the strength of a check that never ran.
#[test]
fn an_nft_that_cannot_be_started_leaves_the_ruleset_alone() {
    let host = FakeFirewallHost::new();
    host.put_file(&ruleset_path(), "the previous ruleset");
    host.lose_nft();

    let outcome = apply_ruleset(
        &host,
        distro(),
        AgentPaths::nftables_ruleset_path(),
        &rendered(&[]),
    );

    assert_eq!(
        outcome,
        Err(FirewallError::NftFailed {
            stderr: String::from("could not run nft"),
        })
    );
    assert_eq!(
        host.file(&ruleset_path()).as_deref(),
        Some("the previous ruleset")
    );
}
