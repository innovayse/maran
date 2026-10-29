//! Tests for [`ChildInput`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::utils::child_input::ChildInput;

/// Debug output carries the variant and the byte COUNT, never a byte of a credential.
#[test]
fn debug_output_of_input_bytes_shows_the_length_and_never_a_byte() {
    let secret = b"hunter2-password";
    let shown = format!("{:?}", ChildInput::Bytes(secret));

    assert!(shown.contains("len: 16"), "{shown}");
    assert!(!shown.contains("hunter2"), "{shown}");
    assert!(!shown.contains("104"), "a byte value leaked: {shown}");
    assert_eq!(format!("{:?}", ChildInput::Null), "ChildInput::Null");
}
