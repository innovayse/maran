//! Tests for the record the child writes and the parent reads.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{ArchiveAnswer, MAXIMUM_ANSWER_BYTES};
use crate::files::archive::archive_refusal::ArchiveRefusal;
use crate::files::model::archive_report::ArchiveReport;

#[test]
fn a_report_round_trips_with_both_figures_intact() {
    let answer = ArchiveAnswer::Done(ArchiveReport {
        entries: 123_456,
        bytes: u64::MAX - 7,
    });

    assert_eq!(ArchiveAnswer::decode(&answer.encode()), Some(answer));
}

#[test]
fn a_refusal_round_trips() {
    let answer = ArchiveAnswer::Refused(ArchiveRefusal::LinkMember);

    assert_eq!(ArchiveAnswer::decode(&answer.encode()), Some(answer));
}

#[test]
fn no_answer_is_longer_than_the_ceiling_the_parent_reads() {
    let longest = ArchiveAnswer::Done(ArchiveReport {
        entries: u64::MAX,
        bytes: u64::MAX,
    });

    assert_eq!(longest.encode().len(), MAXIMUM_ANSWER_BYTES);
    assert!(
        ArchiveAnswer::Refused(ArchiveRefusal::TooDeep)
            .encode()
            .len()
            <= MAXIMUM_ANSWER_BYTES
    );
}

#[test]
fn nothing_malformed_is_repaired() {
    let report = ArchiveAnswer::Done(ArchiveReport {
        entries: 1,
        bytes: 2,
    })
    .encode();

    assert_eq!(ArchiveAnswer::decode(&[]), None);
    assert_eq!(ArchiveAnswer::decode(&report[..16]), None, "one byte short");
    assert_eq!(
        ArchiveAnswer::decode(&[report.clone(), vec![0]].concat()),
        None,
        "one byte long"
    );
    assert_eq!(ArchiveAnswer::decode(&[2, 0]), None, "an unknown tag");
    assert_eq!(
        ArchiveAnswer::decode(&[1]),
        None,
        "a refusal without a code"
    );
    assert_eq!(
        ArchiveAnswer::decode(&[1, 99]),
        None,
        "a code no refusal has"
    );
    assert_eq!(
        ArchiveAnswer::decode(&[1, 5, 0]),
        None,
        "a refusal with trailing bytes"
    );
}
