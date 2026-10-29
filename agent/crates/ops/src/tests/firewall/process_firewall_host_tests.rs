//! What the real firewall host makes of the bounded core's answers.
//!
//! The host can only be handed an adapter's binary, and every class deadline
//! is a minute or more, so a real `nft` that hangs cannot be staged here. The
//! mapping from the core's failure to this area's error is therefore a named
//! function and these tests feed it constructed failures; the polygon suites
//! drive the real `nft`.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::output_stream::OutputStream;
use maran_distro::{DistroFamily, adapter_for};

use super::*;

/// A deadline failure, as the core builds one.
fn timed_out() -> BoundExceeded {
    BoundExceeded::TimedOut {
        program: String::from("/usr/sbin/nft"),
        deadline: Duration::from_secs(120),
    }
}

#[test]
fn a_deadline_is_a_bound_and_never_an_nft_failure() {
    let mapped = spawn_failure(SpawnError::Bound(timed_out()));

    assert_eq!(mapped, FirewallError::Bound(timed_out()));
}

#[test]
fn an_output_past_its_cap_is_a_bound_and_never_an_nft_failure() {
    let exceeded = BoundExceeded::OutputTooLarge {
        program: String::from("/usr/sbin/nft"),
        stream: OutputStream::Stdout,
        cap: 16,
    };

    let mapped = spawn_failure(SpawnError::Bound(exceeded.clone()));

    assert_eq!(mapped, FirewallError::Bound(exceeded));
}

/// The inverse control: a program that could not be started is still the
/// area's "could not run nft", carrying the core's text, so the bound arm is
/// not simply swallowing every failure.
#[test]
fn a_program_that_could_not_be_started_is_still_an_nft_failure() {
    let mapped = spawn_failure(SpawnError::Start {
        program: String::from("/usr/sbin/nft"),
        reason: String::from("No such file or directory"),
    });

    assert!(
        matches!(&mapped, FirewallError::NftFailed { stderr } if stderr.contains("/usr/sbin/nft")),
        "expected NftFailed naming the program, got {mapped:?}"
    );
}

/// Input that could not be delivered is also not a bound.
#[test]
fn input_that_could_not_be_delivered_is_an_nft_failure() {
    let mapped = spawn_failure(SpawnError::Input {
        program: String::from("/usr/sbin/nft"),
    });

    assert!(matches!(mapped, FirewallError::NftFailed { .. }));
}

/// The host runs the adapter's binary and hands back its exit status and
/// output unjudged. `id` stands in for `nft`: a real program the adapter
/// names, with an answer a test can recognise.
#[test]
fn the_host_runs_the_adapters_binary_and_returns_its_outcome() {
    let adapter = adapter_for(DistroFamily::Debian);

    let outcome = ProcessFirewallHost::new()
        .run(&adapter.id_binary(), &["-u"], CallClass::Query)
        .expect("id ran");

    assert_eq!(outcome.status, 0);
    assert!(
        outcome.stdout.trim().parse::<u32>().is_ok(),
        "expected a uid, got {:?}",
        outcome.stdout
    );
}

/// A listing of the size the ban set can reach must fit under the cap this
/// host passes, which is above the core's default.
#[test]
fn the_output_cap_is_above_the_cores_default_so_a_large_ban_set_is_not_refused() {
    assert!(std::hint::black_box(MAXIMUM_STDOUT_BYTES) > ChildOutput::DEFAULT_CAP);
}
