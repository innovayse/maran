//! Tests for [`is_suspended_vhost`].
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside
//! the unit they exercise (rules/testing.md).

// A failing assertion IS the reporting mechanism for a test, so the workspace-wide
// bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use crate::sites::fake_site_host::{
    FakeSiteHost, create_test_site, distro, legacy_suspended_text, php_input,
};
use crate::sites::is_suspended_vhost::is_suspended_vhost;
use crate::sites::render_legacy_suspended_vhost::render_legacy_suspended_vhost;
use crate::sites::resolved_site_paths::resolved_site_paths;
use crate::sites::{disable_site, enable_site};

/// The vhost every test here reads.
const VHOST: &str = "/etc/maran/nginx/sites/example.com.conf";

#[test]
fn a_sites_own_vhost_is_not_the_suspended_one() {
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();
    let paths = resolved_site_paths(&host, &input.account, &input.domain).unwrap();
    let contents = host.config(Path::new(VHOST)).unwrap();

    assert!(!is_suspended_vhost(&contents, &input, &paths).unwrap());
}

#[test]
fn the_vhost_disable_site_writes_is_recognised_as_the_suspended_one() {
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();
    disable_site(&host, distro(), &input).unwrap();
    let paths = resolved_site_paths(&host, &input.account, &input.domain).unwrap();
    let contents = host.config(Path::new(VHOST)).unwrap();

    assert!(
        is_suspended_vhost(&contents, &input, &paths).unwrap(),
        "the text `disable_site` writes must be the text this function recognises"
    );
}

#[test]
fn a_resumed_site_is_no_longer_the_suspended_one() {
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();
    disable_site(&host, distro(), &input).unwrap();
    enable_site(&host, distro(), &input).unwrap();
    let paths = resolved_site_paths(&host, &input.account, &input.domain).unwrap();
    let contents = host.config(Path::new(VHOST)).unwrap();

    assert!(!is_suspended_vhost(&contents, &input, &paths).unwrap());
}

#[test]
fn a_vhost_that_is_neither_rendering_is_not_the_suspended_one() {
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();
    let paths = resolved_site_paths(&host, &input.account, &input.domain).unwrap();

    assert!(
        !is_suspended_vhost("server { server_name example.com; }\n", &input, &paths).unwrap(),
        "a file this agent did not render is not a suspension"
    );
}

#[test]
fn a_stub_written_before_the_symlink_rule_is_still_the_suspended_one() {
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();
    let paths = resolved_site_paths(&host, &input.account, &input.domain).unwrap();
    let aliases = vec!["www.example.com".to_owned()];
    let legacy = render_legacy_suspended_vhost(input.domain.as_str(), &aliases, &paths).unwrap();

    assert!(
        is_suspended_vhost(&legacy, &input, &paths).unwrap(),
        "a stub an earlier agent wrote must still read as a suspension"
    );
}

#[test]
fn a_legacy_stub_with_a_changed_byte_is_not_the_suspended_one() {
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();
    let paths = resolved_site_paths(&host, &input.account, &input.domain).unwrap();
    let aliases = vec!["www.example.com".to_owned()];
    let legacy = render_legacy_suspended_vhost(input.domain.as_str(), &aliases, &paths).unwrap();
    let altered = format!("{legacy}# edited\n");

    assert!(!is_suspended_vhost(&altered, &input, &paths).unwrap());
}

#[test]
fn the_stub_an_earlier_agent_wrote_is_still_the_suspended_one() {
    // A stub written before the challenge location carried its symlink rule is
    // on disk for every customer suspended before the upgrade. Reading it as
    // "not suspended" lets the next certificate renewal put that customer's
    // site back on the air.
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();
    let paths = resolved_site_paths(&host, &input.account, &input.domain).unwrap();
    let legacy = legacy_suspended_text(&host, &input, "example.com");

    assert!(
        is_suspended_vhost(&legacy, &input, &paths).unwrap(),
        "the previous release's stub must still read as a suspension"
    );
}

#[test]
fn another_sites_earlier_stub_is_not_this_sites_suspension() {
    // The legacy comparison is as exact as the current one: a stub that names
    // other domains is not this site's.
    let host = FakeSiteHost::passing();
    let input = php_input();
    create_test_site(&host, &input).unwrap();
    let paths = resolved_site_paths(&host, &input.account, &input.domain).unwrap();
    let other = legacy_suspended_text(&host, &input, "other.example");

    assert!(!is_suspended_vhost(&other, &input, &paths).unwrap());
}
