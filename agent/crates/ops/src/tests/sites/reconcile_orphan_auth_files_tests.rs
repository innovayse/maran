//! Tests for the `reconcile_orphan_auth_files` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use super::reconcile_orphan_auth_files;
use crate::sites::SitesOpError;
use crate::sites::fake_site_host::FakeSiteHost;

/// Where the fixture's credential file for `domain` lives.
fn auth(domain: &str) -> String {
    format!("/etc/maran/nginx/auth/{domain}.htpasswd")
}

/// Where the fixture's vhost for `domain` lives.
fn vhost(domain: &str) -> String {
    format!("/etc/maran/nginx/sites/{domain}.conf")
}

#[test]
fn a_file_whose_site_is_gone_is_removed_and_one_whose_site_exists_is_kept() {
    let host = FakeSiteHost::passing();
    host.place_config(Path::new(&vhost("kept.example.com")), "server {}");
    host.place_auth_file(Path::new(&auth("kept.example.com")), "alice:hash\n");
    host.place_auth_file(Path::new(&auth("gone.example.com")), "bob:hash\n");

    let removed = reconcile_orphan_auth_files(&host).unwrap();

    assert_eq!(removed, 1);
    assert!(
        host.auth_file(Path::new(&auth("kept.example.com")))
            .is_some()
    );
    assert!(
        host.auth_file(Path::new(&auth("gone.example.com")))
            .is_none()
    );
}

#[test]
fn a_domain_with_dots_is_matched_to_its_vhost_whole() {
    // `example.com` must not be read as `example` plus an extension: the vhost
    // of `example.com.au` is not the vhost of `example.com`.
    let host = FakeSiteHost::passing();
    host.place_config(Path::new(&vhost("example.com.au")), "server {}");
    host.place_auth_file(Path::new(&auth("example.com")), "alice:hash\n");

    assert_eq!(reconcile_orphan_auth_files(&host).unwrap(), 1);
}

#[test]
fn a_file_that_is_not_a_credential_file_is_left_alone() {
    let host = FakeSiteHost::passing();
    host.place_auth_file(Path::new("/etc/maran/nginx/auth/notes.txt"), "mine");

    assert_eq!(reconcile_orphan_auth_files(&host).unwrap(), 0);
    assert!(
        host.auth_file(Path::new("/etc/maran/nginx/auth/notes.txt"))
            .is_some()
    );
}

#[test]
fn an_empty_credential_directory_removes_nothing() {
    assert_eq!(
        reconcile_orphan_auth_files(&FakeSiteHost::passing()).unwrap(),
        0
    );
}

#[test]
fn a_vhost_directory_that_cannot_be_listed_removes_nothing() {
    // The inverse of the first case: with the listing refused, a file whose site
    // does exist must NOT be taken for an orphan.
    let host = FakeSiteHost::passing();
    host.place_config(Path::new(&vhost("kept.example.com")), "server {}");
    host.place_auth_file(Path::new(&auth("kept.example.com")), "alice:hash\n");
    host.refuse_listing();

    let failure = reconcile_orphan_auth_files(&host).unwrap_err();

    assert!(matches!(failure, SitesOpError::ConfigUnreadable { .. }));
    assert!(
        host.auth_file(Path::new(&auth("kept.example.com")))
            .is_some()
    );
}
