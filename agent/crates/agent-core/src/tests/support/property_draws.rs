//! Test support for the vacuity guard every property test file carries.
//!
//! Mounted once from `validation/mod.rs` under `#[cfg(test)]`.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use proptest::strategy::{Strategy, ValueTree};
use proptest::test_runner::TestRunner;

use super::property_runs::property_runs;

/// Draws `count` values from `strategy`, from the same fixed seed the properties use.
///
/// A property over a generator that never produces an accepted value passes for
/// the wrong reason, and a green result is read as evidence. So each file asks
/// this for the values its generator makes and requires BOTH verdicts among them:
/// that is the positive control, on the axis that can go blind (rules/testing.md
/// "A check must be able to observe what it reports on").
pub(crate) fn property_draws<S: Strategy>(strategy: &S, count: usize) -> Vec<S::Value> {
    let mut runner = TestRunner::new(property_runs());

    (0..count)
        .filter_map(|_| {
            strategy
                .new_tree(&mut runner)
                .ok()
                .map(|tree| tree.current())
        })
        .collect()
}
