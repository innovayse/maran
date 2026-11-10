//! The release decides the Dovecot generation and the key-value server.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_distro::{DistroFamily, DovecotMajor, adapter_for};

use crate::mail::mail_error::MailError;
use crate::mail::model::mail_layout::MailLayout;

#[test]
fn ubuntu_24_04_is_dovecot_2_3_with_redis_on_its_packaged_socket() {
    let layout = MailLayout::resolve(adapter_for(DistroFamily::Debian), "24.04").unwrap();

    assert_eq!(layout.dovecot_major, DovecotMajor::V23);
    assert_eq!(layout.key_value_socket(), "/run/redis/redis-server.sock");
}

#[test]
fn debian_13_is_dovecot_2_4() {
    // The inverse control for the one release that differs: the same family, the
    // other generation.
    let layout = MailLayout::resolve(adapter_for(DistroFamily::Debian), "13").unwrap();

    assert_eq!(layout.dovecot_major, DovecotMajor::V24);
}

#[test]
fn the_rhel_family_on_release_10_runs_valkey() {
    let layout = MailLayout::resolve(adapter_for(DistroFamily::Rhel), "10.0").unwrap();

    assert_eq!(layout.key_value_server.service, "valkey");
    assert_eq!(layout.key_value_socket(), "/run/valkey/valkey.sock");
}

#[test]
fn a_release_the_adapter_has_no_answer_for_is_refused() {
    let refused = MailLayout::resolve(adapter_for(DistroFamily::Debian), "11");

    assert_eq!(refused.unwrap_err(), MailError::UnsupportedRelease);
}

#[test]
fn the_suse_family_offers_no_mail_layout_on_any_release_and_the_adapter_says_so() {
    // Mail is not offered on this family until it is measured (issue #59): the adapter answers
    // `None` for the Dovecot generation and for the key-value server on every release, and
    // laying the stack out for a guessed generation would write a file the daemon will not read.
    // Both answers are asserted beside the refusal so the test fails on the adapter's change and
    // not only on the operation's. The inverse control is the three tests above, where a family
    // that does offer mail resolves.
    let suse = adapter_for(DistroFamily::Suse);

    for version_id in ["16.0", "16.1", "15.6"] {
        assert!(suse.dovecot_major(version_id).is_none(), "{version_id}");
        assert!(suse.key_value_server(version_id).is_none(), "{version_id}");
        assert_eq!(
            MailLayout::resolve(suse, version_id).unwrap_err(),
            MailError::UnsupportedRelease,
            "{version_id}"
        );
    }
}
