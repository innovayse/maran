//! Tests for keeping an archive request away from the trash.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::fs::browse_path::BrowsePath;

use super::refuse_reserved_path;
use crate::proto::ErrorCode;

fn refused(path: &str) -> bool {
    match refuse_reserved_path(&BrowsePath::parse(path).unwrap()) {
        Ok(()) => false,
        Err(error) => {
            assert_eq!(error.code, ErrorCode::InvalidInput as i32);
            true
        }
    }
}

#[test]
fn the_trash_and_everything_inside_it_is_refused() {
    assert!(refused(".maran-trash"));
    assert!(refused(".maran-trash/entries/x/payload"));
}

#[test]
fn the_home_and_ordinary_paths_are_fine() {
    assert!(!refused(""));
    assert!(!refused("sites/a"));
    assert!(!refused("sites/.maran-trash"));
    assert!(!refused(".maran-trash-not"));
}
