//! Tests for [`ChildOutput`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::utils::child_output::ChildOutput;

/// The default captures both streams, each at four mebibytes.
#[test]
fn the_default_output_caps_both_streams_at_four_mebibytes() {
    assert_eq!(ChildOutput::DEFAULT_CAP, 4 * 1024 * 1024);
    assert_eq!(
        ChildOutput::DEFAULT,
        ChildOutput::Capture {
            stdout_cap: 4_194_304,
            stderr_cap: 4_194_304,
        }
    );
    assert_ne!(ChildOutput::DEFAULT, ChildOutput::Discard);
}
