//! Tests for the SUSE family's mail facts: the packages, the map type, the password scheme and the
//! two release readings that answer `None` for every release because the mail stack is not
//! offered on this family until it is measured (issue #59).

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::mail::mail_password_scheme::MailPasswordScheme;

use super::{
    antivirus_packages, dovecot_major, key_value_server, mail_packages, mail_password_scheme,
    postfix_map_type,
};

#[test]
fn the_mail_stack_is_offered_on_no_release_of_this_family() {
    for release in ["16.0", "16.1", "15.6", "16", "", "tumbleweed"] {
        assert_eq!(dovecot_major(release), None, "{release}");
        assert_eq!(key_value_server(release), None, "{release}");
    }
}

#[test]
fn a_family_that_does_offer_the_mail_stack_answers_for_its_releases() {
    // The inverse control for the test above: the same questions asked of the RHEL family do get
    // an answer, so a `None` here is a decision and not an artefact of the question.
    assert!(crate::rhel::rhel_packages::dovecot_major("9").is_some());
    assert!(crate::rhel::rhel_packages::key_value_server("9").is_some());
}

#[test]
fn the_mail_package_lists_name_a_package_each_and_the_map_type_is_lmdb() {
    assert!(mail_packages().contains(&"postfix"));
    assert!(mail_packages().contains(&"rspamd"));
    assert!(antivirus_packages().contains(&"clamav"));
    assert_eq!(postfix_map_type(), "lmdb");
    assert_eq!(mail_password_scheme(), MailPasswordScheme::BlowfishCrypt);
}
