//! An inventory warning onto its wire payload.

use maran_ops::transfer::inventory::model::source_warning_kind::SourceWarningKind;
use maran_ops::transfer::inventory::model::source_warning_record::SourceWarningRecord;

use crate::proto;

/// The wire form of a warning. The subject is whatever the record carries, which
/// is a validated account or domain name or nothing: the record has no
/// constructor that takes free text, so none can reach the panel through here.
#[must_use]
pub fn to_wire_source_warning(warning: &SourceWarningRecord) -> proto::SourceWarning {
    proto::SourceWarning {
        kind: wire_kind(warning.kind()) as i32,
        subject: warning.subject().to_owned(),
    }
}

/// The wire member of a warning kind. Exhaustive on purpose.
fn wire_kind(kind: SourceWarningKind) -> proto::SourceWarningKind {
    match kind {
        SourceWarningKind::NameRefused => proto::SourceWarningKind::NameRefused,
        SourceWarningKind::SiteUnreadable => proto::SourceWarningKind::SiteUnreadable,
        SourceWarningKind::CronUnreadable => proto::SourceWarningKind::CronUnreadable,
        SourceWarningKind::DatabasesUnreadable => proto::SourceWarningKind::DatabasesUnreadable,
        SourceWarningKind::DiskUnreadable => proto::SourceWarningKind::DiskUnreadable,
        SourceWarningKind::Truncated => proto::SourceWarningKind::Truncated,
        SourceWarningKind::MailNotMigrated => proto::SourceWarningKind::MailNotMigrated,
    }
}

#[cfg(test)]
#[path = "../../tests/services/transfer/to_wire_source_warning_tests.rs"]
mod tests;
