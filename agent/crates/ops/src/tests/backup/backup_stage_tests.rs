//! Progress percentages are computed from work done, never written down.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::*;

/// Each stage owns the span the plan fixed for it.
#[test]
fn each_stage_owns_its_fixed_span() {
    assert_eq!(BackupStage::DumpingDatabases.start_percent(), 0);
    assert_eq!(BackupStage::DumpingDatabases.end_percent(), 40);
    assert_eq!(BackupStage::ArchivingFiles.start_percent(), 40);
    assert_eq!(BackupStage::ArchivingFiles.end_percent(), 80);
    assert_eq!(BackupStage::Uploading.start_percent(), 80);
    assert_eq!(BackupStage::Uploading.end_percent(), 99);
}

/// Every stage answers the wire name the service layer copies verbatim.
#[test]
fn every_stage_answers_its_wire_name() {
    assert_eq!(BackupStage::DumpingDatabases.as_str(), "dumping_databases");
    assert_eq!(BackupStage::ArchivingFiles.as_str(), "archiving_files");
    assert_eq!(BackupStage::Uploading.as_str(), "uploading");
}

/// The percentage moves with the work, and lands on the span's own ends.
#[test]
fn a_percentage_is_interpolated_across_the_span() {
    let stage = BackupStage::DumpingDatabases;

    assert_eq!(stage.percent_through(0, 4), 0);
    assert_eq!(stage.percent_through(1, 4), 10);
    assert_eq!(stage.percent_through(2, 4), 20);
    assert_eq!(stage.percent_through(4, 4), 40);
}

/// Two runs with different totals report different intermediate percentages —
/// which is the property a literal cannot have.
#[test]
fn the_same_step_of_two_different_totals_reports_two_different_percentages() {
    let stage = BackupStage::DumpingDatabases;

    assert_ne!(stage.percent_through(1, 2), stage.percent_through(1, 5));
}

/// No work at all reports the span's start rather than dividing by zero.
#[test]
fn an_empty_total_reports_the_start_of_the_span() {
    assert_eq!(BackupStage::DumpingDatabases.percent_through(0, 0), 0);
    assert_eq!(BackupStage::ArchivingFiles.percent_through(0, 0), 40);
}

/// More done than there was to do cannot push a percentage past its span.
#[test]
fn work_beyond_the_total_cannot_exceed_the_span() {
    assert_eq!(BackupStage::ArchivingFiles.percent_through(99, 4), 80);
}
