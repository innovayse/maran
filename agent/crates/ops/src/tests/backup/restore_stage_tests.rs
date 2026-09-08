//! The restore's stage spans, and the percentages nobody writes down.

use super::*;

/// Every stage's span is the one the operation's contract names, and the spans
/// tile the range without a gap or an overlap.
#[test]
fn the_restore_stages_tile_zero_to_ninety_nine_without_a_gap() {
    let stages = [
        RestoreStage::Verifying,
        RestoreStage::Downloading,
        RestoreStage::RestoringDatabases,
        RestoreStage::RestoringFiles,
        RestoreStage::Finalising,
    ];

    assert_eq!(stages[0].start_percent(), 0);
    assert_eq!(stages[stages.len() - 1].end_percent(), 99);
    for pair in stages.windows(2) {
        assert_eq!(pair[0].end_percent(), pair[1].start_percent(), "{pair:?}");
    }
}

/// A percentage inside a stage is interpolated from work done, so two runs over
/// different amounts of work report different intermediate numbers.
#[test]
fn a_percentage_inside_a_stage_is_computed_from_work_and_never_a_literal() {
    let stage = RestoreStage::RestoringDatabases;

    let one_of_two = stage.percent_through(1, 2);
    let one_of_four = stage.percent_through(1, 4);

    assert_ne!(one_of_two, one_of_four);
    assert_eq!(stage.percent_through(0, 4), stage.start_percent());
    assert_eq!(stage.percent_through(4, 4), stage.end_percent());
}

/// No work at all reports the stage's start rather than dividing by zero.
#[test]
fn a_stage_with_nothing_to_do_reports_its_start() {
    assert_eq!(
        RestoreStage::RestoringDatabases.percent_through(0, 0),
        RestoreStage::RestoringDatabases.start_percent()
    );
}

/// The wire names are the ones the service layer copies into the stream.
#[test]
fn every_restore_stage_carries_its_wire_name() {
    assert_eq!(RestoreStage::Verifying.as_str(), "verifying");
    assert_eq!(RestoreStage::Downloading.as_str(), "downloading");
    assert_eq!(
        RestoreStage::RestoringDatabases.as_str(),
        "restoring_databases"
    );
    assert_eq!(RestoreStage::RestoringFiles.as_str(), "restoring_files");
    assert_eq!(RestoreStage::Finalising.as_str(), "finalising");
}
