//! Stopping every daemon even when one refuses.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::mail::fake_mail_host::{FakeMailHost, VERSION_ID, distro};
use crate::mail::mail_error::MailError;
use crate::mail::model::mail_layout::MailLayout;
use crate::mail::stop_mail_daemons::stop_mail_daemons;

#[test]
fn a_unit_that_refuses_to_stop_does_not_leave_the_others_running() {
    let host = FakeMailHost::new()
        .with_running_unit("postfix")
        .with_running_unit("dovecot")
        .with_running_unit("rspamd")
        .with_running_unit("redis-server")
        .with_refusing_disable("postfix");
    let layout = MailLayout::resolve(distro(), VERSION_ID).unwrap();

    let refused = stop_mail_daemons(&host, distro(), &layout).unwrap_err();

    assert_eq!(
        refused,
        MailError::ServiceRefused {
            unit: "postfix".to_owned()
        }
    );
    for unit in ["dovecot", "rspamd", "redis-server"] {
        assert!(!host.is_active(unit), "{unit} was left running");
        assert!(host.is_masked(unit), "{unit} was left unmasked");
    }
}

#[test]
fn every_unit_is_masked_even_after_a_refusal() {
    // The mask is the half that survives a package upgrade, so a refusal to
    // disable must not skip it.
    let host = FakeMailHost::new()
        .with_running_unit("postfix")
        .with_refusing_disable("postfix");
    let layout = MailLayout::resolve(distro(), VERSION_ID).unwrap();

    stop_mail_daemons(&host, distro(), &layout).unwrap_err();

    assert!(host.is_masked("postfix"));
}
