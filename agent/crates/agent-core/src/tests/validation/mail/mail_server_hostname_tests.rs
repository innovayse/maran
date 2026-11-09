//! Tests for the `mail_server_hostname` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::MailServerHostname;
use crate::validation::mail::mail_domain_error::MailDomainError;
use crate::validation::mail::mail_server_hostname_error::MailServerHostnameError;

#[test]
fn a_fully_qualified_hostname_is_accepted_and_lowercased() {
    assert_eq!(
        MailServerHostname::parse("Mail.Example.com")
            .unwrap()
            .as_str(),
        "mail.example.com"
    );
}

#[test]
fn a_bare_host_name_is_refused() {
    assert_eq!(
        MailServerHostname::parse("mail"),
        Err(MailServerHostnameError::Invalid(
            MailDomainError::SingleLabel
        ))
    );
}

#[test]
fn a_hostname_with_a_line_break_is_refused() {
    assert!(MailServerHostname::parse("mail.example.com\nmynetworks = 0.0.0.0/0").is_err());
}
