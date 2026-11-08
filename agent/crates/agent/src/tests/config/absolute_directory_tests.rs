//! Tests for the `absolute_directory` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use super::{OptionsError, absolute_directory};

#[test]
fn an_absolute_path_is_accepted_as_written() {
    let path = absolute_directory("--fleet-dir", "/etc/maran/fleet").unwrap();

    assert_eq!(path, Path::new("/etc/maran/fleet"));
}

#[test]
fn a_relative_path_is_refused_naming_the_flag_and_the_value() {
    for value in ["fleet", "./fleet", "../fleet", ""] {
        assert_eq!(
            absolute_directory("--fleet-dir", value),
            Err(OptionsError::RelativePath {
                flag: "--fleet-dir",
                value: value.to_owned()
            })
        );
    }
}
