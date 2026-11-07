//! The agent's half of the panel's own certificate (issue #132), against the
//! real nginx and the real openssl.
//!
//! The closing criterion of the issue is that issuance, renewal and fallback are
//! tested in the polygon and that the panel vhost serves the issued certificate.
//! This suite drives `write_panel_challenge`, `install_panel_certificate` and
//! `remove_panel_challenge` through their real hosts, with the production panel
//! vhost loaded in the polygon's nginx (`fixtures/polygon_panel_host.rs` says how),
//! and then asks nginx what it serves in the two ways a client can:
//!
//! * port 80, with a real HTTP request, for the challenge file;
//! * port 8443, with a real TLS handshake (`openssl s_client`), for the serial and
//!   SHA-256 fingerprint of the certificate presented, compared with the ones
//!   `openssl x509` reads from the material that was installed. `nginx -T` is
//!   read as well, because it is the server's own list of the files it reads.
//!
//! Certificates come from a throwaway root the suite owns, so no network and no
//! real authority is involved.
//!
//! # What this suite cannot observe, said plainly
//!
//! **UNOBSERVED HERE:**
//!
//! * the ACME protocol itself: the order, the account key, the authority fetching
//!   the challenge. That is the panel's and is covered by the backend's opt-in
//!   Pebble test and unit tests; here the "authority" is a plain HTTP request;
//! * the API's scheduler: that a renewal is started when a certificate nears its
//!   expiry, and that a failed renewal is surfaced;
//! * the installer's placement of the vhost: the polygon renders the shipped
//!   template into the agent's include directory;
//! * the CLI verb and the revert to a self-signed pair, which do not exist yet
//!   (follow-ups of the issue);
//! * a PUBLIC client's trust, which no isolated container can observe.
//!
//! The "fallback" is therefore what the agent owns: a pair it refuses, or that
//! nginx refuses, leaves the previous certificate serving byte for byte.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

// Only the guard is used from the account fixture here.
#[allow(dead_code)]
#[path = "fixtures/polygon_account.rs"]
mod polygon_account;
// Shared with the FTPS suite, which issues into the store; this one takes text.
#[allow(dead_code)]
#[path = "fixtures/polygon_certificate_authority.rs"]
mod polygon_certificate_authority;
#[path = "fixtures/polygon_config_file.rs"]
mod polygon_config_file;
// Reaches `crate::polygon_web` for starting and reloading nginx.
#[allow(dead_code)]
#[path = "fixtures/polygon_panel_host.rs"]
mod polygon_panel_host;
// Only the port-80 fetch and the start/reload are used here.
#[allow(dead_code)]
#[path = "fixtures/polygon_web.rs"]
mod polygon_web;

use std::os::unix::fs::PermissionsExt as _;
use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_agent_core::validation::web::acme_key_authorization::AcmeKeyAuthorization;
use maran_agent_core::validation::web::acme_token::AcmeToken;
use maran_agent_core::validation::web::domain::Domain;
use maran_distro::{DistroAdapter, adapter_for, detect};
use maran_ops::ssl::{
    CertificateMaterial, ProcessPanelChallengeHost, ProcessSslHost, SslOpError,
    install_panel_certificate, remove_panel_challenge, write_panel_challenge,
};

use polygon_account::PolygonAccount;
use polygon_certificate_authority::PolygonCertificateAuthority;
use polygon_config_file::PolygonConfigFile;
use polygon_panel_host::PolygonPanelHost;
use polygon_web::PolygonWeb;

/// The panel's host name in this suite.
const PANEL_NAME: &str = "panel.polygon.test";

/// The challenge token the issuance test writes: base64url, as the validated
/// type requires.
const TOKEN: &str = "polygonPanelToken_01";

/// The key authorization answering [`TOKEN`]: the token, a dot, base64url.
const KEY_AUTHORIZATION: &str = "polygonPanelToken_01.polygonThumbprint-02";

/// The distribution adapter for the polygon this suite is running in.
///
/// # Panics
///
/// Panics when the host is outside the support matrix, which a polygon image
/// never is.
fn polygon_distro() -> &'static dyn DistroAdapter {
    adapter_for(
        detect()
            .expect("a polygon image is a supported host")
            .family,
    )
}

/// The panel's host name as the validated type the certificate authority wants.
fn panel_domain() -> Domain {
    Domain::parse(PANEL_NAME).expect("the panel name is a valid domain")
}

/// The validated token and key authorization of the suite's one challenge.
fn challenge() -> (AcmeToken, AcmeKeyAuthorization) {
    let token = AcmeToken::parse(TOKEN).expect("the token is base64url");
    let authorization = AcmeKeyAuthorization::parse(&token, KEY_AUTHORIZATION)
        .expect("the key authorization answers the token");

    (token, authorization)
}

/// The free-space floor the suite writes under: one mebibyte, so the test is
/// about the challenge and not about how full the runner's disk is.
fn small_floor() -> FreeSpaceFloor {
    FreeSpaceFloor::from_mebibytes(1).expect("one mebibyte is a valid floor")
}

/// Installs a certificate chain and key as the panel's own pair, the way the
/// rpc does, through the real hosts.
fn install(chain_pem: &str, key_pem: &str) -> Result<i64, SslOpError> {
    install_panel_certificate(
        &ProcessSslHost::new(),
        polygon_distro(),
        &CertificateMaterial::new(chain_pem, key_pem),
    )
}

/// What a client sees today: the identity of the certificate the panel's TLS
/// block presents, once it presents `expected` or the deadline passes.
fn served_until(host: &PolygonPanelHost, expected: &str) -> String {
    host.presented_certificate_until(|presented| presented == expected)
}

/// The identity of the first certificate in `pem`, as the TLS client reports it.
fn identity_of(pem: &str) -> String {
    PolygonPanelHost::identity_of(polygon_distro(), pem)
}

/// The panel vhost, loaded with a self-signed pair as the installer leaves it,
/// and the identity of that pair.
fn panel_with_self_signed_pair() -> (PolygonPanelHost, String) {
    PolygonAccount::require_polygon();
    let (certificate, key) = PolygonPanelHost::self_signed_pair(polygon_distro(), PANEL_NAME);
    let identity = identity_of(&certificate);
    let host = PolygonPanelHost::bring_up(polygon_distro(), PANEL_NAME, &certificate, &key);

    (host, identity)
}

#[test]
#[ignore = "loads the panel vhost into the real nginx and handshakes with the real openssl: polygon only"]
fn the_panel_vhost_serves_the_self_signed_pair_it_starts_with_and_a_client_does_not_trust_it() {
    let (host, initial) = panel_with_self_signed_pair();
    let authority = PolygonCertificateAuthority::create(polygon_distro(), "panel-cert-trust-ca");

    // The server's own list of what it reads, and then what it presents: the
    // pair at the panel's paths is the one on the wire.
    let configuration = host.effective_configuration();
    assert!(
        configuration.contains(&format!(
            "# configuration file {}:",
            host.vhost_path().display()
        )),
        "nginx -T must list the panel vhost among the files it loaded"
    );
    for path in [
        PolygonPanelHost::certificate_path(),
        PolygonPanelHost::key_path(),
    ] {
        assert!(
            configuration.contains(&format!("{};", path.display())),
            "nginx -T must show the TLS block reading {path:?}"
        );
    }
    assert_eq!(
        host.presented_certificate(),
        initial,
        "the TLS block must present the self-signed pair at the panel's paths"
    );

    // The negative half of the trust control used by the issuance test: a
    // client that validates refuses a self-signed leaf, so a later "verifies"
    // is the CA's chain and not an accident of how the client is invoked.
    assert!(
        !host.handshake_verifies_against(&authority.root_path()),
        "a self-signed pair must not verify against the suite's root"
    );
}

#[test]
#[ignore = "writes a challenge file and asks the real nginx for it over port 80: polygon only"]
fn a_challenge_the_agent_writes_is_answered_by_the_panels_port_80_block_until_it_is_removed() {
    let (host, _) = panel_with_self_signed_pair();
    let (token, authorization) = challenge();
    let path = format!("/.well-known/acme-challenge/{}", token.as_str());

    // Controls first, so a later 200 is the file and not a block that answers
    // everything: the name is the panel's, the token is not yet there, and
    // nothing outside the challenge directory is served.
    let before = PolygonWeb::fetch(host.name(), &path);
    assert!(
        before.starts_with("HTTP/1.1 404"),
        "the token must not be served before it is written:\n{before}"
    );
    let root = PolygonWeb::fetch(host.name(), "/");
    assert!(
        root.starts_with("HTTP/1.1 404"),
        "the port-80 block must serve nothing but the challenge directory:\n{root}"
    );

    write_panel_challenge(
        &ProcessPanelChallengeHost::new(),
        &small_floor(),
        &token,
        &authorization,
    )
    .unwrap_or_else(|error| panic!("writing the challenge must succeed: {error}"));

    let served = PolygonWeb::fetch_until(host.name(), &path, |response| {
        response.starts_with("HTTP/1.1 200")
    });
    assert!(
        served.starts_with("HTTP/1.1 200"),
        "the port-80 block must serve the written token:\n{served}"
    );
    assert!(
        served.ends_with(KEY_AUTHORIZATION),
        "the body must be the key authorization and nothing else:\n{served}"
    );
    let file = Path::new(AgentPaths::PANEL_ACME_CHALLENGE_DIRECTORY).join(token.as_str());
    assert_eq!(
        std::fs::metadata(&file).unwrap().permissions().mode() & 0o777,
        0o644,
        "the web server's workers must be able to read the challenge file"
    );

    remove_panel_challenge(&ProcessPanelChallengeHost::new(), &token)
        .unwrap_or_else(|error| panic!("removing the challenge must succeed: {error}"));
    let after = PolygonWeb::fetch(host.name(), &path);
    assert!(
        after.starts_with("HTTP/1.1 404"),
        "a removed token must no longer be served:\n{after}"
    );
    assert!(
        matches!(
            remove_panel_challenge(&ProcessPanelChallengeHost::new(), &token),
            Err(SslOpError::ChallengeNotFound)
        ),
        "removing a token that is gone must report it as not found"
    );
}

#[test]
#[ignore = "installs a certificate into the real nginx and handshakes with the real openssl: polygon only"]
fn an_issued_certificate_is_installed_and_the_panel_vhost_serves_it_not_the_self_signed_one() {
    let (host, initial) = panel_with_self_signed_pair();
    let authority = PolygonCertificateAuthority::create(polygon_distro(), "panel-cert-issue-ca");
    let (chain, key) = authority.issue_leaf(polygon_distro(), &panel_domain(), "issued");
    let issued = identity_of(&chain);
    assert_ne!(
        issued, initial,
        "the control is only meaningful when the issued certificate differs from the one served"
    );

    let expiry = install(&chain, &key)
        .unwrap_or_else(|error| panic!("installing the issued pair must succeed: {error}"));
    assert!(expiry > 0, "the expiry returned must be a real timestamp");

    // On disk first: the pair at the panel's paths is the one that was offered,
    // and the key is not readable by anyone but root.
    let (certificate_on_disk, key_on_disk) = PolygonPanelHost::pair_on_disk();
    assert_eq!(
        String::from_utf8(certificate_on_disk).unwrap().trim_end(),
        chain.trim_end(),
        "the certificate file must hold the offered chain"
    );
    assert_eq!(
        String::from_utf8(key_on_disk).unwrap().trim_end(),
        key.trim_end(),
        "the key file must hold the offered key"
    );
    assert_eq!(
        std::fs::metadata(PolygonPanelHost::key_path())
            .unwrap()
            .permissions()
            .mode()
            & 0o777,
        0o600,
        "the private key must be readable by root alone"
    );

    // Then on the wire, which is what the criterion asks: the vhost serves it.
    PolygonPanelHost::assert_tree_valid(polygon_distro(), "the tree after the install");
    assert_eq!(
        served_until(&host, &issued),
        issued,
        "the TLS block must present the issued certificate, not the self-signed one"
    );
    assert!(
        host.handshake_verifies_against(&authority.root_path()),
        "a client trusting the issuing root must accept the chain for the panel's name"
    );
    let configuration = host.effective_configuration();
    assert!(
        configuration.contains(&format!(
            "{};",
            PolygonPanelHost::certificate_path().display()
        )),
        "nginx -T must still show the TLS block reading the panel's certificate path"
    );

    // Idempotence: the same material again changes nothing and says the same.
    let again = install(&chain, &key).expect("installing the same pair again must succeed");
    assert_eq!(again, expiry, "a repeat must report the same expiry");
    assert_eq!(host.presented_certificate(), issued);
}

#[test]
#[ignore = "installs two certificates in turn into the real nginx and handshakes with the real openssl: polygon only"]
fn renewal_replaces_the_issued_pair_and_the_handshake_serial_changes_to_the_new_one() {
    let (host, _) = panel_with_self_signed_pair();
    let authority = PolygonCertificateAuthority::create(polygon_distro(), "panel-cert-renew-ca");
    let (first_chain, first_key) = authority.issue_leaf(polygon_distro(), &panel_domain(), "first");
    let (second_chain, second_key) =
        authority.issue_leaf(polygon_distro(), &panel_domain(), "second");
    let first = identity_of(&first_chain);
    let second = identity_of(&second_chain);
    assert_ne!(first, second, "a renewal must be a different certificate");

    install(&first_chain, &first_key).expect("the first issuance must install");
    assert_eq!(
        served_until(&host, &first),
        first,
        "the first pair must be served"
    );

    install(&second_chain, &second_key).expect("the renewal must install");
    assert_eq!(
        served_until(&host, &second),
        second,
        "after a renewal the TLS block must present the new certificate"
    );
    assert_ne!(
        host.presented_certificate(),
        first,
        "the replaced certificate must no longer be presented"
    );
    assert!(host.handshake_verifies_against(&authority.root_path()));

    // A renewal whose key belongs to another certificate is refused outright,
    // and the renewed pair keeps serving.
    let before = PolygonPanelHost::pair_on_disk();
    let refusal = install(&first_chain, &second_key)
        .expect_err("a certificate with another certificate's key must be refused");
    assert!(
        matches!(refusal, SslOpError::KeyDoesNotMatchCertificate),
        "the refusal must say the key does not match, got {refusal}"
    );
    assert_eq!(
        PolygonPanelHost::pair_on_disk(),
        before,
        "nothing may be written"
    );
    assert_eq!(host.presented_certificate(), second);
}

#[test]
#[ignore = "feeds the real openssl pairs it must refuse and checks the real nginx keeps serving: polygon only"]
fn a_pair_the_agent_refuses_leaves_the_previous_pair_serving_byte_for_byte() {
    let (host, initial) = panel_with_self_signed_pair();
    let authority = PolygonCertificateAuthority::create(polygon_distro(), "panel-cert-refuse-ca");
    let (chain, key) = authority.issue_leaf(polygon_distro(), &panel_domain(), "wanted");
    let (_, other_key) = authority.issue_leaf(polygon_distro(), &panel_domain(), "other");
    let before = PolygonPanelHost::pair_on_disk();

    let garbage =
        "-----BEGIN CERTIFICATE-----\nbm90IGEgY2VydGlmaWNhdGU=\n-----END CERTIFICATE-----";
    let not_a_key = "-----BEGIN PRIVATE KEY-----\nbm90IGEga2V5\n-----END PRIVATE KEY-----";

    // Each refusal names its own typed variant, and each leaves the pair, the
    // listener and the certificate on the wire exactly as they were.
    let mismatched = install(&chain, &other_key).expect_err("another key must be refused");
    assert!(matches!(mismatched, SslOpError::KeyDoesNotMatchCertificate));
    assert_eq!(
        PolygonPanelHost::pair_on_disk(),
        before,
        "a mismatched key must write nothing"
    );

    let unreadable_certificate = install(garbage, &key).expect_err("garbage must be refused");
    assert!(matches!(
        unreadable_certificate,
        SslOpError::MalformedCertificate { .. } | SslOpError::ExpiryUnreadable { .. }
    ));
    assert_eq!(
        PolygonPanelHost::pair_on_disk(),
        before,
        "a certificate that is not one must write nothing"
    );

    let unreadable_key = install(&chain, not_a_key).expect_err("a bad key must be refused");
    assert!(matches!(
        unreadable_key,
        SslOpError::MalformedPrivateKey | SslOpError::KeyDoesNotMatchCertificate
    ));
    assert_eq!(
        PolygonPanelHost::pair_on_disk(),
        before,
        "a key that is not one must write nothing"
    );

    assert_eq!(
        host.presented_certificate(),
        initial,
        "after three refusals the TLS block must still present the previous certificate"
    );
    let answer = PolygonWeb::fetch(host.name(), "/");
    assert!(
        answer.starts_with("HTTP/1.1 404"),
        "nginx must still answer on port 80 after the refusals:\n{answer}"
    );
    PolygonPanelHost::assert_tree_valid(polygon_distro(), "the tree after the refusals");

    // The inverse control: the same call with the right pair is accepted, so
    // the refusals above were about the material and not about the setup.
    install(&chain, &key).expect("the matching pair must be accepted");
    assert_eq!(
        served_until(&host, &identity_of(&chain)),
        identity_of(&chain)
    );
}

#[test]
#[ignore = "breaks the real nginx tree on purpose so nginx -t refuses a valid pair: polygon only"]
fn a_pair_nginx_refuses_is_rolled_back_and_the_previous_certificate_keeps_serving() {
    let (host, initial) = panel_with_self_signed_pair();
    let authority = PolygonCertificateAuthority::create(polygon_distro(), "panel-cert-rollback-ca");
    let (chain, key) = authority.issue_leaf(polygon_distro(), &panel_domain(), "rolled");
    let before = PolygonPanelHost::pair_on_disk();

    // A valid, matching pair cannot make nginx -t fail by itself, so the
    // refusal is induced from outside it: a sibling include with a directive
    // nginx does not know. What this proves is the agent's side of the fallback
    // (the swap is undone, the old bytes are back, the old certificate is still
    // on the wire), not that some certificate is one nginx rejects.
    let broken_path =
        Path::new(AgentPaths::NGINX_INCLUDE_DIRECTORY).join("zz-panel-polygon-broken.conf");
    let broken = PolygonConfigFile::at(&broken_path);
    std::fs::write(&broken_path, "this_is_not_a_directive_nginx_knows;\n")
        .expect("the broken include can be written");

    let refusal = install(&chain, &key).expect_err("nginx -t must refuse the tree");
    assert!(
        matches!(refusal, SslOpError::NginxValidation { .. }),
        "the refusal must be nginx's validation, got {refusal}"
    );
    assert_eq!(
        PolygonPanelHost::pair_on_disk(),
        before,
        "the previous pair must be back, byte for byte"
    );

    drop(broken);
    PolygonPanelHost::assert_tree_valid(
        polygon_distro(),
        "the tree once the broken include is gone",
    );
    assert_eq!(
        host.presented_certificate(),
        initial,
        "the previous certificate must have kept serving through the refusal"
    );

    // The inverse control: the same material, with the tree healthy again, is
    // accepted and served. The refusal was the tree's, not the pair's.
    install(&chain, &key).expect("the same pair must install once the tree is valid");
    assert_eq!(
        served_until(&host, &identity_of(&chain)),
        identity_of(&chain)
    );
}
