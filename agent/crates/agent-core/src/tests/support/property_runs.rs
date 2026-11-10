//! Test support shared by the property tests of the validators: how many cases a
//! property is run for, and from which seed.
//!
//! Mounted once from `validation/mod.rs` under `#[cfg(test)]`, because its users
//! are the property tests of six units in three groups, and one configuration
//! written six times is six chances for one of them to quietly run fewer cases.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use proptest::prelude::ProptestConfig;
use proptest::test_runner::RngSeed;

/// How many cases each property is run for. Large enough that a validator with
/// one weakened rule is found (every property here has a generator dense in the
/// characters the rule is about), small enough that the whole set stays well
/// under a second.
const CASES: u32 = 512;

/// The seed every property starts from.
///
/// Fixed, so a run is reproducible: a property that fails fails on every run
/// and on every machine, and a property that passes does not pass by luck on
/// this one. A new seed is a reviewed change to this constant, never an
/// environment variable (rules/testing.md "Determinism").
const SEED: u64 = 0x4d41_5241_4e00_0001;

/// The configuration of every property in the validators' tests.
///
/// No failure-persistence file is written: a counterexample is printed with the
/// seed, and a file appearing next to the source on a developer's machine would
/// be a tracked-looking artefact nobody asked for.
pub(crate) fn property_runs() -> ProptestConfig {
    ProptestConfig {
        cases: CASES,
        rng_seed: RngSeed::Fixed(SEED),
        failure_persistence: None,
        ..ProptestConfig::default()
    }
}
