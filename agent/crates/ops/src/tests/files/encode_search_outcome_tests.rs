//! Tests for the child-to-parent encoding of a search outcome.
//!
//! The round trip lives in the decoder's tests, which hold both halves; these
//! pin the byte layout the parent depends on.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::privs::entry_kind::EntryKind;

use super::encode_search_outcome;
use crate::files::model::search_match::SearchMatch;
use crate::files::model::search_outcome::SearchOutcome;

#[test]
fn an_empty_outcome_is_the_flag_and_two_counts() {
    let bytes = encode_search_outcome(&SearchOutcome {
        matches: Vec::new(),
        truncated: true,
        skipped_entries: 2,
    });

    assert_eq!(bytes, vec![1, 2, 0, 0, 0, 0, 0, 0, 0]);
}

#[test]
fn a_match_is_written_length_prefixed_with_its_kind_byte() {
    let bytes = encode_search_outcome(&SearchOutcome {
        matches: vec![SearchMatch {
            path: "a".to_owned(),
            kind: EntryKind::Symlink,
            size: 5,
            mode: 0o644,
            modified_unix: -1,
            uid: 1000,
            gid: 1001,
            links: 1,
        }],
        truncated: false,
        skipped_entries: 0,
    });

    // flag, skipped, count, then the match.
    assert_eq!(&bytes[..9], &[0, 0, 0, 0, 0, 1, 0, 0, 0]);
    assert_eq!(&bytes[9..13], &[1, 0, 0, 0]);
    assert_eq!(bytes[13], b'a');
    assert_eq!(bytes[14], 2);
    assert_eq!(bytes.len(), 9 + 4 + 1 + 1 + 8 + 4 + 8 + 4 + 4 + 8);
}

#[test]
fn every_kind_has_its_own_byte() {
    let kind_at = |kind| {
        let bytes = encode_search_outcome(&SearchOutcome {
            matches: vec![SearchMatch {
                path: String::new(),
                kind,
                size: 0,
                mode: 0,
                modified_unix: 0,
                uid: 0,
                gid: 0,
                links: 0,
            }],
            truncated: false,
            skipped_entries: 0,
        });
        bytes[13]
    };

    assert_eq!(kind_at(EntryKind::File), 0);
    assert_eq!(kind_at(EntryKind::Directory), 1);
    assert_eq!(kind_at(EntryKind::Symlink), 2);
    assert_eq!(kind_at(EntryKind::Other), 3);
}
