//! Every service-manager command ends with the end-of-options marker and the unit.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::mail::model::mail_unit::MailUnit;

#[test]
fn each_command_names_the_unit_after_the_end_of_options_marker() {
    let unit = MailUnit::new("postfix");

    assert_eq!(unit.unmask(), ["unmask", "--", "postfix"]);
    assert_eq!(unit.enable(), ["enable", "--", "postfix"]);
    assert_eq!(unit.is_active(), ["is-active", "--", "postfix"]);
    assert_eq!(
        unit.reload_or_restart(),
        ["reload-or-restart", "--", "postfix"]
    );
    assert_eq!(unit.mask(), ["mask", "--", "postfix"]);
}

#[test]
fn disabling_stops_the_unit_in_the_same_command() {
    // A stopped unit left in the boot sequence is the operator's decision undone
    // by the next reboot.
    assert_eq!(
        MailUnit::new("dovecot").disable_now(),
        ["disable", "--now", "--", "dovecot"]
    );
}
