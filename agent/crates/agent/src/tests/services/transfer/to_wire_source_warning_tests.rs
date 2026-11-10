//! Tests for an inventory warning on the wire.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;
use maran_ops::transfer::inventory::model::source_warning_kind::SourceWarningKind;
use maran_ops::transfer::inventory::model::source_warning_record::SourceWarningRecord;

use super::to_wire_source_warning;
use crate::proto;

#[test]
fn every_kind_has_its_own_wire_member() {
    for (kind, expected) in [
        (
            SourceWarningKind::NameRefused,
            proto::SourceWarningKind::NameRefused,
        ),
        (
            SourceWarningKind::SiteUnreadable,
            proto::SourceWarningKind::SiteUnreadable,
        ),
        (
            SourceWarningKind::CronUnreadable,
            proto::SourceWarningKind::CronUnreadable,
        ),
        (
            SourceWarningKind::DatabasesUnreadable,
            proto::SourceWarningKind::DatabasesUnreadable,
        ),
        (
            SourceWarningKind::DiskUnreadable,
            proto::SourceWarningKind::DiskUnreadable,
        ),
        (
            SourceWarningKind::Truncated,
            proto::SourceWarningKind::Truncated,
        ),
        (
            SourceWarningKind::MailNotMigrated,
            proto::SourceWarningKind::MailNotMigrated,
        ),
    ] {
        let wire = to_wire_source_warning(&SourceWarningRecord::about_nothing(kind));

        assert_eq!(wire.kind, expected as i32);
        assert_ne!(wire.kind, proto::SourceWarningKind::Unspecified as i32);
    }
}

#[test]
fn the_subject_is_the_validated_name_or_empty() {
    let account = AccountName::parse("alice").unwrap();
    let domain = Domain::parse("example.com").unwrap();

    assert_eq!(
        to_wire_source_warning(&SourceWarningRecord::about_account(
            SourceWarningKind::DiskUnreadable,
            &account
        ))
        .subject,
        "alice"
    );
    assert_eq!(
        to_wire_source_warning(&SourceWarningRecord::about_domain(
            SourceWarningKind::SiteUnreadable,
            &domain
        ))
        .subject,
        "example.com"
    );
    assert!(
        to_wire_source_warning(&SourceWarningRecord::about_nothing(
            SourceWarningKind::Truncated
        ))
        .subject
        .is_empty()
    );
}
