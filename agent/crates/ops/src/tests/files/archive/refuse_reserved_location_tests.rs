//! Tests for keeping the paths of an archive operation out of the trash.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::fs::browse_path::BrowsePath;

use super::refuse_reserved_location;
use crate::files::archive::archive_refusal::ArchiveRefusal;

fn path(text: &str) -> BrowsePath {
    BrowsePath::parse(text).unwrap()
}

#[test]
fn the_trash_and_anything_below_it_is_refused() {
    for text in [
        ".maran-trash",
        ".maran-trash/entries",
        ".maran-trash/entries/x/payload",
    ] {
        assert_eq!(
            refuse_reserved_location(&path(text)).unwrap_err(),
            ArchiveRefusal::UnsafeName,
            "{text}"
        );
    }
}

#[test]
fn the_home_lookalikes_and_the_same_name_deeper_down_are_accepted() {
    for text in [
        "",
        "site",
        ".maran-trash-not/x",
        "sub/.maran-trash",
        "sub/.maran-trash/x",
    ] {
        assert!(refuse_reserved_location(&path(text)).is_ok(), "{text}");
    }
}
