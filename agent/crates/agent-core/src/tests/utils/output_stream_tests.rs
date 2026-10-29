//! Tests for [`OutputStream`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::utils::output_stream::OutputStream;

/// Each stream displays as its plain name, which an error message quotes.
#[test]
fn standard_output_and_standard_error_display_as_their_names() {
    assert_eq!(OutputStream::Stdout.to_string(), "standard output");
    assert_eq!(OutputStream::Stderr.to_string(), "standard error");
}
