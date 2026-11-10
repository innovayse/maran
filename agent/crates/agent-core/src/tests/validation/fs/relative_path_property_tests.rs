//! Property tests for [`RelativePath`]: a path the type holds can never name anything
//! outside the account's home, whatever text produced it.
//!
//! Declared beside `relative_path_tests.rs` by `relative_path.rs`. Two generators feed
//! the properties: one builds text out of the pieces a path is made of (including the
//! components that matter: empty, `.`, `..`, a control character), the other out of single
//! characters. Every property runs from the fixed seed of `property_runs`.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Component;

use proptest::prelude::*;
use proptest::sample::select;

use super::{MAXIMUM_COMPONENTS, RelativePath, RelativePathError};
use crate::validation::property_draws::property_draws;
use crate::validation::property_runs::property_runs;

/// The pieces of a path, ordinary and not: the ones the rule exists to refuse sit beside the
/// ones it must accept, so that a path is often one broken piece away from a good one.
const PIECES: [&str; 9] = [
    "a",
    "dir",
    "file.txt",
    "..",
    ".",
    "",
    "tab\there",
    "nul\0byte",
    "naïve name",
];

/// Single characters for the character-level generator: separators, dots and the controls.
const CHARACTERS: [char; 8] = ['a', 'b', '.', '/', '/', '\0', '\n', 'é'];

/// A path text built from one to twelve [`PIECES`] joined by `/`, optionally starting with `/`.
fn piecewise_text() -> impl Strategy<Value = String> {
    (
        proptest::collection::vec(select(PIECES.to_vec()), 0..12),
        any::<bool>(),
    )
        .prop_map(|(pieces, absolute)| {
            let joined = pieces.join("/");
            if absolute {
                format!("/{joined}")
            } else {
                joined
            }
        })
}

/// Path text made of single [`CHARACTERS`].
fn characterwise_text() -> impl Strategy<Value = String> {
    proptest::collection::vec(select(CHARACTERS.to_vec()), 0..16)
        .prop_map(|characters| characters.into_iter().collect::<String>())
}

/// The rule, restated independently of the implementation: what a path must be to be accepted.
fn model_accepts(text: &str) -> bool {
    if text.is_empty() || text.starts_with('/') {
        return false;
    }
    let components: Vec<&str> = text.split('/').collect();

    components.len() <= MAXIMUM_COMPONENTS
        && components.iter().all(|component| {
            !component.is_empty()
                && *component != "."
                && *component != ".."
                && !component.bytes().any(|byte| byte.is_ascii_control())
                && component.len() <= 255
        })
}

proptest! {
    #![proptest_config(property_runs())]

    #[test]
    fn a_path_is_accepted_exactly_when_the_model_says_so(text in piecewise_text()) {
        prop_assert_eq!(RelativePath::parse(&text).is_ok(), model_accepts(&text));
    }

    #[test]
    fn a_path_of_single_characters_is_accepted_exactly_when_the_model_says_so(
        text in characterwise_text(),
    ) {
        prop_assert_eq!(RelativePath::parse(&text).is_ok(), model_accepts(&text));
    }

    #[test]
    fn an_accepted_path_cannot_leave_the_home_it_is_joined_to(text in piecewise_text()) {
        if let Ok(path) = RelativePath::parse(&text) {
            let as_path = path.as_path();

            prop_assert!(as_path.is_relative());
            prop_assert!(as_path.components().all(|c| matches!(c, Component::Normal(_))));
            prop_assert!(as_path.components().count() <= MAXIMUM_COMPONENTS);
            prop_assert!(path.parent_components().len() < MAXIMUM_COMPONENTS);
        }
    }

    #[test]
    fn an_accepted_path_is_its_parent_components_and_its_file_name_and_nothing_more(
        text in piecewise_text(),
    ) {
        if let Ok(path) = RelativePath::parse(&text) {
            let mut rebuilt: Vec<&str> = path.parent_components().iter().map(String::as_str).collect();
            rebuilt.push(path.file_name());

            prop_assert_eq!(rebuilt.join("/"), text);
        }
    }

    #[test]
    fn a_traversal_component_is_refused_wherever_it_stands(
        before in proptest::collection::vec("[a-z]{1,5}", 0..4),
        after in proptest::collection::vec("[a-z]{1,5}", 0..4),
        traversal in select(vec!["..", "."]),
    ) {
        let mut components = before;
        components.push(traversal.to_owned());
        components.extend(after);

        prop_assert!(RelativePath::parse(&components.join("/")).is_err());
    }

    #[test]
    fn a_path_is_accepted_up_to_eight_components_and_not_beyond(depth in 1usize..20) {
        let text = vec!["a"; depth].join("/");

        prop_assert_eq!(RelativePath::parse(&text).is_ok(), depth <= MAXIMUM_COMPONENTS);
        if depth > MAXIMUM_COMPONENTS {
            prop_assert_eq!(RelativePath::parse(&text), Err(RelativePathError::TooDeep));
        }
    }

    #[test]
    fn a_component_is_accepted_up_to_two_hundred_fifty_five_bytes_and_not_beyond(
        length in 250usize..262,
    ) {
        let text = format!("dir/{}", "a".repeat(length));

        prop_assert_eq!(RelativePath::parse(&text).is_ok(), length <= 255);
    }
}

/// Both generators reach both verdicts, so the model comparisons compare something.
#[test]
fn the_generators_produce_accepted_and_refused_paths() {
    let piecewise = property_draws(&piecewise_text(), 512);
    let characterwise = property_draws(&characterwise_text(), 512);

    for drawn in [&piecewise, &characterwise] {
        assert!(drawn.iter().any(|text| RelativePath::parse(text).is_ok()));
        assert!(drawn.iter().any(|text| RelativePath::parse(text).is_err()));
    }
}
