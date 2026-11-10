//! Tests for [`SourceWarningRecord`]: a warning whose subject can only be a
//! validated name.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;

use crate::transfer::inventory::model::source_warning_kind::SourceWarningKind;
use crate::transfer::inventory::model::source_warning_record::SourceWarningRecord;

/// A warning about nothing has no subject.
#[test]
fn a_warning_about_nothing_has_no_subject() {
    let warning = SourceWarningRecord::about_nothing(SourceWarningKind::Truncated);

    assert_eq!(warning.kind(), SourceWarningKind::Truncated);
    assert_eq!(warning.subject(), "");
}

/// A warning about an account carries its validated name.
#[test]
fn a_warning_about_an_account_carries_its_name() {
    let account = AccountName::parse("acme").unwrap();

    let warning = SourceWarningRecord::about_account(SourceWarningKind::CronUnreadable, &account);

    assert_eq!(warning.subject(), "acme");
}

/// A warning about a site carries its validated, lowercased domain.
#[test]
fn a_warning_about_a_site_carries_its_domain() {
    let domain = Domain::parse("Shop.Example.COM").unwrap();

    let warning = SourceWarningRecord::about_domain(SourceWarningKind::SiteUnreadable, &domain);

    assert_eq!(warning.subject(), "shop.example.com");
}
