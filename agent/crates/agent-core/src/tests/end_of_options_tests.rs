//! Tests for [`END_OF_OPTIONS`].

use super::END_OF_OPTIONS;

/// The operand is exactly two hyphens: anything else is read by a `getopt`
/// parser as an option or as a name.
#[test]
fn the_end_of_options_operand_is_two_hyphens() {
    assert_eq!(END_OF_OPTIONS, "--");
}
