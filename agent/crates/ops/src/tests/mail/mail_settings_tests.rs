//! The settings the panel decides, as the templates need them.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::mail::alias_fanout::MAXIMUM_FANOUT;
use maran_agent_core::validation::mail::mail_message_size_mib::MailMessageSizeMib;

use crate::mail::fake_mail_host::settings;
use crate::mail::model::mail_settings::MailSettings;

#[test]
fn the_message_size_is_handed_on_in_bytes() {
    assert_eq!(settings().message_size_bytes(), 52_428_800);
}

#[test]
fn the_largest_size_the_validated_type_allows_does_not_overflow_the_byte_count() {
    let mut largest = settings();
    largest.message_size = MailMessageSizeMib::parse(512).unwrap();

    assert_eq!(largest.message_size_bytes(), 512 * 1024 * 1024);
}

/// The fan-out the filter is told about can be as large as the expansion Postfix is
/// allowed to make, and no larger: one number, read in two crates.
#[test]
fn the_fan_out_bound_is_the_expansion_limit() {
    assert_eq!(
        usize::try_from(MailSettings::ALIAS_EXPANSION_LIMIT).unwrap(),
        MAXIMUM_FANOUT
    );
}
