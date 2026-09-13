//! The placeholder, and the certificate it must never replace.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::sites::fake_site_host::{create_test_site, distro, php_input};
use crate::sites::{SiteCertificate, SitePaths};
use crate::ssl::fake_ssl_host::{EXPIRY_UNIX, FakeSslHost, matching_material};
use crate::ssl::generate_self_signed::generate_self_signed;
use crate::ssl::install_certificate::install_certificate;
use crate::ssl::self_signed_marker::self_signed_marker;
use crate::ssl::ssl_op_error::SslOpError;

/// A host with `example.com` created and nothing installed on it.
fn host_with_site() -> FakeSslHost {
    let host = FakeSslHost::passing();
    create_test_site(&host, &php_input()).unwrap();

    host
}

#[test]
fn a_site_with_no_certificate_gets_the_placeholder_installed() {
    let host = host_with_site();
    let input = php_input();

    let expiry = generate_self_signed(&host, distro(), &input).unwrap();

    assert_eq!(expiry, EXPIRY_UNIX);
    let certificate = SiteCertificate::for_domain(&input.domain);
    assert!(host.stored(certificate.key_path()).is_some());
    let vhost = host
        .config(&SitePaths::for_site(&input.account, &input.domain).config_path)
        .unwrap();
    // The point of the placeholder: nginx will not start a TLS block whose
    // `ssl_certificate` is missing, so the site can only be configured for
    // HTTPS once SOMETHING is there.
    assert!(vhost.contains(&certificate.certificate_path().display().to_string()));
}

#[test]
fn an_existing_placeholder_is_replaced() {
    let host = host_with_site();

    generate_self_signed(&host, distro(), &php_input()).unwrap();
    let again = generate_self_signed(&host, distro(), &php_input());

    assert!(again.is_ok());
    // And it is still a placeholder afterwards, so the third call works too.
    let certificate = SiteCertificate::for_domain(&php_input().domain);
    assert!(host.has_marker(&self_signed_marker(&certificate)));
}

#[test]
fn a_certificate_an_authority_signed_is_never_overwritten() {
    let host = host_with_site().with_authority_signed_certificate();
    let input = php_input();
    let certificate = SiteCertificate::for_domain(&input.domain);
    host.preinstall(&certificate, &matching_material());
    // A marker left behind by an earlier placeholder, over which a real
    // certificate was then installed by something that forgot to clear it. The
    // second condition is what catches that: the certificate is not self-signed,
    // so it is not ours whatever the file says. Belt and braces, and this is the
    // test that proves the braces are attached.
    host.premark(&self_signed_marker(&certificate));

    let failure = generate_self_signed(&host, distro(), &input).unwrap_err();

    // Replacing a trusted certificate with one every browser refuses is an
    // outage produced by a retry, which is why the contract makes this
    // ALREADY_EXISTS rather than a replacement.
    assert!(matches!(failure, SslOpError::AlreadyExists { .. }));
    assert_eq!(host.stored_count(), 2);
}

#[test]
fn a_certificate_with_no_marker_file_is_refused() {
    let host = host_with_site().with_self_signed_subject("CN = staging.example.com");
    let input = php_input();
    host.preinstall(
        &SiteCertificate::for_domain(&input.domain),
        &matching_material(),
    );

    let failure = generate_self_signed(&host, distro(), &input).unwrap_err();

    // Self-signed is necessary and nowhere near sufficient: the certificate a
    // customer generated for their staging box is self-signed too, and being
    // "recognised" means their certificate AND their private key are
    // overwritten, with no recovery.
    assert!(matches!(failure, SslOpError::AlreadyExists { .. }));
    assert_eq!(host.stored_count(), 2);
}

#[test]
fn a_subject_that_merely_contains_the_marker_text_is_refused() {
    // The reviewer's proof of concept, kept verbatim as the regression it is.
    // openssl does not escape a comma inside a value, it QUOTES the value — so
    // the previous check, which split the printed subject on every comma,
    // produced the fragment ` OU = maran-self-signed`, trimmed it to an exact
    // match, and destroyed the customer's certificate and key with the very
    // marker added to protect them. Nothing parses a subject any more.
    let host = host_with_site().with_self_signed_subject(
        "C = US, O = \"Example, OU = maran-self-signed, more\", CN = test3.example.com",
    );
    let input = php_input();
    host.preinstall(
        &SiteCertificate::for_domain(&input.domain),
        &matching_material(),
    );

    let failure = generate_self_signed(&host, distro(), &input).unwrap_err();

    assert!(matches!(failure, SslOpError::AlreadyExists { .. }));
    assert_eq!(host.stored_count(), 2);
}

#[test]
fn a_real_certificate_installed_over_a_placeholder_is_afterwards_refused() {
    let host = host_with_site();
    let input = php_input();
    generate_self_signed(&host, distro(), &input).unwrap();

    // The customer's real certificate replaces the placeholder.
    install_certificate(&host, distro(), &input, &matching_material()).unwrap();

    // The marker went with the bytes it described. Without that, a certificate
    // the customer paid an authority for would inherit the placeholder's licence
    // to be destroyed by the next call.
    let certificate = SiteCertificate::for_domain(&input.domain);
    assert!(!host.has_marker(&self_signed_marker(&certificate)));
    let failure = generate_self_signed(&host, distro(), &input).unwrap_err();
    assert!(matches!(failure, SslOpError::AlreadyExists { .. }));
}

#[test]
fn the_marker_is_written_beside_the_material_it_describes() {
    let host = host_with_site();
    let input = php_input();

    generate_self_signed(&host, distro(), &input).unwrap();

    let certificate = SiteCertificate::for_domain(&input.domain);
    let marker = self_signed_marker(&certificate);
    assert!(host.has_marker(&marker));
    // In the agent's own store, beside the certificate: nothing but the agent
    // writes there and no customer can reach it, which is what makes a file's
    // existence a safer answer than any text openssl formats.
    assert_eq!(marker.parent(), certificate.certificate_path().parent());
}

#[test]
fn the_placeholder_names_every_alias_of_the_site() {
    let host = host_with_site();

    generate_self_signed(&host, distro(), &php_input()).unwrap();

    let requested = host
        .arguments()
        .into_iter()
        .find(|argv| argv.first().map(String::as_str) == Some("req"))
        .unwrap();
    // A certificate that does not name a host is not accepted for it, so a
    // placeholder covering only the primary domain leaves every alias failing
    // differently from the primary.
    assert!(requested[1].contains("/CN=example.com"));
    assert!(requested[2].contains("DNS:example.com"));
    assert!(requested[2].contains("DNS:www.example.com"));
}

#[test]
fn the_generated_subject_still_says_what_the_certificate_is() {
    let host = host_with_site();

    generate_self_signed(&host, distro(), &php_input()).unwrap();

    let requested = host
        .arguments()
        .into_iter()
        .find(|argv| argv.first().map(String::as_str) == Some("req"))
        .unwrap();
    // DOCUMENTATION, so an operator running `openssl x509 -subject` over the
    // store can see what a file is. It is deliberately NOT the decision — the
    // marker file is — and it must never be turned back into one.
    assert!(requested[1].contains("/OU=maran-self-signed"));
}
