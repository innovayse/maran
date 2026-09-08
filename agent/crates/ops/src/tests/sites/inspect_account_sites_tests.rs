//! Tests for [`inspect_account_sites`].
//!
//! What is pinned here is the thing the panel will act on: whether this host
//! can be SEEN to have stopped serving an account's sites. Every assertion is
//! therefore about the answer for a vhost that is really on the fake host's
//! disk, never about the text the operation happened to build on its way.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use maran_agent_core::validation::system::name::AccountName;

use crate::sites::fake_site_host::{FakeSiteHost, create_test_site, distro, php_input};
use crate::sites::{disable_site, inspect_account_sites};

/// The vhost every test here acts on.
const VHOST: &str = "/etc/maran/nginx/sites/example.com.conf";

/// The account every test here asks about.
fn account() -> AccountName {
    AccountName::parse("acme").unwrap()
}

#[test]
fn a_disabled_site_is_reported_as_serving_the_stub() {
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();
    disable_site(&host, distro(), &input).unwrap();

    let observed = inspect_account_sites(&host, &account()).unwrap();

    assert!(observed.directory_readable);
    assert_eq!(observed.sites.len(), 1);
    assert_eq!(observed.sites[0].domain, "example.com");
    assert!(
        observed.sites[0].serving_stub,
        "the vhost on disk IS the suspended render, so it must be reported as stubbed"
    );
}

#[test]
fn a_site_that_is_still_serving_its_own_content_is_not_reported_as_stubbed() {
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();

    let observed = inspect_account_sites(&host, &account()).unwrap();

    assert_eq!(observed.sites.len(), 1);
    assert!(
        !observed.sites[0].serving_stub,
        "a live PHP vhost is not the suspended render"
    );
}

#[test]
fn a_vhost_that_names_another_accounts_home_is_not_reported() {
    let host = FakeSiteHost::passing();
    host.place_config(
        Path::new("/etc/maran/nginx/sites/other.example.conf"),
        "server {\n    root /home/other/sites/other.example;\n}\n",
    );

    let observed = inspect_account_sites(&host, &account()).unwrap();

    assert!(observed.sites.is_empty());
}

#[test]
fn a_neighbour_whose_name_this_account_prefixes_is_not_reported() {
    // `acme` is a prefix of `acmecorp`, and a membership test written without
    // the trailing separator would claim the neighbour's site as this
    // account's — and then report a suspension that never touched it.
    let host = FakeSiteHost::passing();
    host.place_config(
        Path::new("/etc/maran/nginx/sites/corp.example.conf"),
        "server {\n    root /home/acmecorp/sites/corp.example;\n}\n",
    );

    let observed = inspect_account_sites(&host, &account()).unwrap();

    assert!(observed.sites.is_empty());
}

#[test]
fn a_vhost_altered_after_it_was_stubbed_is_not_reported_as_stubbed() {
    // The comparison is byte-for-byte and the `server_name` line is only a
    // candidate for the render, never trusted: an edited stub must come back
    // as NOT stubbed, because this agent can no longer say what it serves.
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();
    disable_site(&host, distro(), &input).unwrap();

    let stub = host.config(Path::new(VHOST)).unwrap();
    host.place_config(
        Path::new(VHOST),
        &stub.replace("return 403", "proxy_pass http://127.0.0.1:8080; #"),
    );

    let observed = inspect_account_sites(&host, &account()).unwrap();

    assert_eq!(observed.sites.len(), 1);
    assert!(!observed.sites[0].serving_stub);
}

#[test]
fn a_vhost_with_no_server_name_line_is_not_reported_as_stubbed() {
    let host = FakeSiteHost::passing();
    host.place_config(
        Path::new(VHOST),
        "server {\n    root /home/acme/sites/example.com;\n}\n",
    );

    let observed = inspect_account_sites(&host, &account()).unwrap();

    assert_eq!(observed.sites.len(), 1);
    assert!(!observed.sites[0].serving_stub);
}

#[test]
fn a_vhost_directory_that_cannot_be_listed_is_reported_as_unreadable_and_not_as_empty() {
    // The blind case: an unreadable directory and an account with no sites
    // both produce an empty list, and the empty list is the one that reads as
    // "everything is suspended". They must be distinguishable.
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();
    host.refuse_listing();

    let observed = inspect_account_sites(&host, &account()).unwrap();

    assert!(!observed.directory_readable);
    assert!(observed.sites.is_empty());
}

#[test]
fn an_account_with_no_vhost_at_all_is_readable_and_empty() {
    // The inverse control for the test above: the same empty list, with the
    // readable flag TRUE, so a gate that only ever saw refusals would not pass.
    let host = FakeSiteHost::passing();

    let observed = inspect_account_sites(&host, &account()).unwrap();

    assert!(observed.directory_readable);
    assert!(observed.sites.is_empty());
}
