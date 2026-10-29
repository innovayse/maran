//! Tests for [`create_vhost`].
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise (rules/testing.md). `create_vhost.rs` declares this file
//! with `#[path]`, which keeps it a child module.
//!
//! What is tested is what `create_vhost` DECIDES: which warnings it asks the
//! protocol to refuse, and which validator and reload it hands it. The
//! refusal itself — a validator that exits 0 and warns — is the protocol's,
//! and `safe_write/create_config_tests.rs` drives it; the real nginx's answer
//! is the polygon's.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use maran_agent_core::utils::call_class::CallClass;

use super::create_vhost;
use crate::sites::SitesOpError;
use crate::sites::fake_site_host::{FakeSiteHost, distro};

/// Where these tests put the vhost.
const TARGET: &str = "/etc/maran/nginx/sites/shop.example.conf";

#[test]
fn a_new_vhost_refuses_the_warning_for_each_of_its_names_bare_and_with_a_leading_dot() {
    let host = FakeSiteHost::passing();

    create_vhost(
        &host,
        distro(),
        Path::new(TARGET),
        "server {}\n",
        &["zero.example", "shop.example"],
    )
    .unwrap();

    assert_eq!(
        host.create_refusals(),
        vec![vec![
            "conflicting server name \"zero.example\"".to_owned(),
            "conflicting server name \".zero.example\"".to_owned(),
            "conflicting server name \"shop.example\"".to_owned(),
            "conflicting server name \".shop.example\"".to_owned(),
        ]]
    );
}

#[test]
fn a_warning_about_a_longer_name_does_not_match_a_shorter_one() {
    let host = FakeSiteHost::passing();
    create_vhost(
        &host,
        distro(),
        Path::new(TARGET),
        "server {}\n",
        &["shop.example"],
    )
    .unwrap();
    let fragments = &host.create_refusals()[0];
    let about_another_site =
        "nginx: [warn] conflicting server name \"shop.example.org\" on 0.0.0.0:80, ignored";
    let about_this_site =
        "nginx: [warn] conflicting server name \"shop.example\" on 0.0.0.0:80, ignored";

    assert!(
        fragments
            .iter()
            .all(|fragment| !about_another_site.contains(fragment.as_str())),
        "a warning about shop.example.org must not refuse shop.example: {fragments:?}"
    );
    assert!(
        fragments
            .iter()
            .any(|fragment| about_this_site.contains(fragment.as_str())),
        "the warning about the site's own name must refuse it: {fragments:?}"
    );
}

#[test]
fn a_new_vhost_is_validated_with_nginx_t_and_reloaded_through_the_service_manager() {
    let host = FakeSiteHost::passing();

    create_vhost(
        &host,
        distro(),
        Path::new(TARGET),
        "server {}\n",
        &["shop.example"],
    )
    .unwrap();

    assert_eq!(
        host.create_validators(),
        vec![(distro().nginx_binary().to_string(), vec!["-t".to_owned()])]
    );
    assert_eq!(
        host.create_reloads(),
        vec![(
            distro().service_manager().to_string(),
            vec![
                "reload".to_owned(),
                "--".to_owned(),
                distro().nginx_service().to_owned()
            ]
        )]
    );
}

#[test]
fn a_new_vhost_goes_through_the_creating_half_of_the_protocol_only() {
    let host = FakeSiteHost::passing();

    create_vhost(
        &host,
        distro(),
        Path::new(TARGET),
        "server {}\n",
        &["shop.example"],
    )
    .unwrap();

    assert_eq!(host.create_targets(), vec![Path::new(TARGET).to_path_buf()]);
    assert!(host.replaced_targets().is_empty());
    assert_eq!(
        host.config(Path::new(TARGET)).as_deref(),
        Some("server {}\n")
    );
}

#[test]
fn a_vhost_that_is_already_there_is_refused_as_a_taken_name_and_not_overwritten() {
    let host = FakeSiteHost::passing();
    host.place_config(Path::new(TARGET), "the first site's vhost\n");

    let refusal = create_vhost(
        &host,
        distro(),
        Path::new(TARGET),
        "server {}\n",
        &["shop.example"],
    );

    assert!(
        matches!(refusal, Err(SitesOpError::ServerNameTaken { .. })),
        "expected ServerNameTaken, got {refusal:?}"
    );
    assert_eq!(
        host.config(Path::new(TARGET)).as_deref(),
        Some("the first site's vhost\n")
    );
}

#[test]
fn a_new_vhost_runs_the_validator_in_the_short_class() {
    let host = FakeSiteHost::passing();

    create_vhost(
        &host,
        distro(),
        Path::new(TARGET),
        "server {}\n",
        &["shop.example"],
    )
    .unwrap();

    let classes: Vec<CallClass> = host
        .protocol()
        .iter()
        .map(|call| call.validator_class)
        .collect();
    assert_eq!(classes, vec![CallClass::Validate]);
}

#[test]
fn a_new_vhost_whose_validator_times_out_reports_the_bound_and_never_a_taken_name() {
    // A creation that was killed learned nothing about the names: `ServerNameTaken` or
    // `NginxValidation` would send the customer to change a domain that nginx never read.
    let host = FakeSiteHost::passing();
    host.time_out_validation();

    let refusal = create_vhost(
        &host,
        distro(),
        Path::new(TARGET),
        "server {}\n",
        &["shop.example"],
    );

    assert!(
        matches!(refusal, Err(SitesOpError::Bound(_))),
        "expected Bound, got {refusal:?}"
    );
    assert_eq!(host.config(Path::new(TARGET)), None);
}
