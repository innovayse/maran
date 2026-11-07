//! What a placeholder is generated to say.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::domain::Domain;

use crate::ssl::self_signed_request_for::self_signed_request_for;

#[test]
fn the_subject_names_the_domain_and_carries_the_marker_text() {
    let request = self_signed_request_for(&Domain::parse("example.com").unwrap(), &[]);

    assert_eq!(request.subject, "/CN=example.com/OU=maran-self-signed");
    assert_eq!(request.days, 3650);
    assert_eq!(request.key_specification, "rsa:2048");
}

#[test]
fn every_alias_is_a_subject_alternative_name_after_the_primary() {
    let aliases = [Domain::parse("www.example.com").unwrap()];

    let request = self_signed_request_for(&Domain::parse("example.com").unwrap(), &aliases);

    assert_eq!(
        request.subject_alternative_name,
        "subjectAltName=DNS:example.com,DNS:www.example.com"
    );
}

#[test]
fn a_domain_with_no_alias_names_only_itself() {
    let request = self_signed_request_for(&Domain::parse("panel.example.com").unwrap(), &[]);

    assert_eq!(
        request.subject_alternative_name,
        "subjectAltName=DNS:panel.example.com"
    );
}
