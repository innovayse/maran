//! Tests for [`enable_site`].
//!
//! Enabling is the operation a retry is most likely to find already done, so
//! what these tests pin is that the second call is silent: no re-render, no
//! `nginx -t`, no reload.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use crate::sites::fake_site_host::{
    FakeSiteHost, create_test_site, distro, legacy_suspended_text, php_input,
    php_input_with_options,
};
use maran_agent_core::validation::web::request_rate::RequestRate;

use crate::sites::model::create_site_input::CreateSiteInput;
use crate::sites::model::site_options::SiteOptions;
use crate::sites::{SitesOpError, disable_site, enable_site};

#[test]
fn enabling_a_site_that_is_already_enabled_changes_nothing() {
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();

    enable_site(&host, distro(), &input).unwrap();

    // No second write, therefore no second `nginx -t` and no second reload:
    // the panel retries after a timeout, and a reload per retry is a storm.
    assert_eq!(host.writes(), 1);
}

#[test]
fn enabling_a_suspended_site_restores_its_own_configuration() {
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();
    let original = host
        .config(Path::new("/etc/maran/nginx/sites/example.com.conf"))
        .unwrap();
    disable_site(&host, distro(), &input).unwrap();

    enable_site(&host, distro(), &input).unwrap();

    assert_eq!(
        host.config(Path::new("/etc/maran/nginx/sites/example.com.conf")),
        Some(original)
    );
}

#[test]
fn enabling_a_site_that_does_not_exist_is_not_found() {
    let host = FakeSiteHost::passing();

    match enable_site(&host, distro(), &php_input()) {
        Err(SitesOpError::NotFound { domain }) => assert_eq!(domain, "example.com"),
        other => panic!("expected NotFound, got {other:?}"),
    }
}

#[test]
fn enabling_a_site_that_serves_the_earlier_stub_restores_its_own_configuration() {
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();
    let vhost = Path::new("/etc/maran/nginx/sites/example.com.conf");
    let original = host.config(vhost).unwrap();
    host.place_config(vhost, &legacy_suspended_text(&host, &input, "example.com"));

    enable_site(&host, distro(), &input).unwrap();

    assert_eq!(host.config(vhost), Some(original));
}

#[test]
fn enabling_a_suspended_site_with_a_password_restores_its_options_and_its_credential_file() {
    let host = FakeSiteHost::passing();
    let input = php_input_with_options();
    create_test_site(&host, &input).unwrap();
    let vhost = Path::new("/etc/maran/nginx/sites/example.com.conf");
    let original = host.config(vhost).unwrap();
    disable_site(&host, distro(), &input).unwrap();
    // The stub carries none of the customer's configuration.
    let stub = host.config(vhost).unwrap();
    assert!(!stub.contains("auth_basic"), "{stub}");
    assert!(!stub.contains("client_max_body_size"), "{stub}");
    let credential = maran_agent_core::agent_paths::AgentPaths::site_auth_file(&input.domain);
    host.remove_auth_file_for_test(&credential);

    enable_site(&host, distro(), &input).unwrap();

    assert_eq!(host.config(vhost), Some(original));
    assert!(host.auth_file(&credential).is_some());
    // The file landed before the vhost that names it.
    let order = host.write_order();
    let last_credential = order.iter().rposition(|path| path == &credential).unwrap();
    let last_vhost = order.iter().rposition(|path| path == vhost).unwrap();
    assert!(last_credential < last_vhost, "{order:?}");
}

#[test]
fn enabling_a_site_that_is_already_right_repairs_a_lost_credential_file_without_rewriting_the_vhost()
 {
    let host = FakeSiteHost::passing();
    let input = php_input_with_options();
    create_test_site(&host, &input).unwrap();
    let credential = maran_agent_core::agent_paths::AgentPaths::site_auth_file(&input.domain);
    host.remove_auth_file_for_test(&credential);
    let vhost_writes = host
        .write_order()
        .iter()
        .filter(|path| path.extension().is_some_and(|ext| ext == "conf"))
        .count();

    enable_site(&host, distro(), &input).unwrap();

    assert!(host.auth_file(&credential).is_some());
    let after = host
        .write_order()
        .iter()
        .filter(|path| path.extension().is_some_and(|ext| ext == "conf"))
        .count();
    assert_eq!(after, vhost_writes);
}

#[test]
fn enabling_an_enabled_site_with_a_new_request_limit_writes_the_limit_into_its_vhost() {
    // This is how the panel applies a request limit to a running site: it
    // re-renders the vhost from the site's current options.
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();
    let limited = CreateSiteInput {
        options: SiteOptions::default()
            .with_request_rate(Some(RequestRate::parse(20, 40).unwrap())),
        ..php_input()
    };

    enable_site(&host, distro(), &limited).unwrap();

    let vhost = host
        .config(Path::new("/etc/maran/nginx/sites/example.com.conf"))
        .unwrap();
    assert!(
        vhost.contains("limit_req zone=rl_example.com burst=40 nodelay;"),
        "{vhost}"
    );
    assert_eq!(host.writes(), 2);
}

#[test]
fn enabling_a_site_again_without_its_request_limit_removes_the_limit() {
    let host = FakeSiteHost::passing();
    let limited = CreateSiteInput {
        options: SiteOptions::default()
            .with_request_rate(Some(RequestRate::parse(20, 40).unwrap())),
        ..php_input()
    };
    create_test_site(&host, &limited).unwrap();

    enable_site(&host, distro(), &php_input()).unwrap();

    let vhost = host
        .config(Path::new("/etc/maran/nginx/sites/example.com.conf"))
        .unwrap();
    assert!(!vhost.contains("limit_req"), "{vhost}");
}
