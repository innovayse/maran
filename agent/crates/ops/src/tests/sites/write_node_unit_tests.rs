//! Tests for the `write_node_unit` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_distro::SiteWebServer;

use super::write_node_unit;
use crate::sites::fake_site_host::{FakeSiteHost, distro, node_input, php_input};
use crate::sites::model::site_paths::SitePaths;
use crate::sites::node_unit_path::node_unit_path;

#[test]
fn a_node_site_gets_a_unit_that_runs_its_validated_command() {
    let host = FakeSiteHost::passing();
    let input = node_input(SiteWebServer::Nginx);
    let paths = SitePaths::for_site(&input.account, &input.domain, input.web_server);

    write_node_unit(&host, distro(), &input, &paths).unwrap();

    let unit = host
        .config(&node_unit_path(distro(), &input.domain))
        .expect("the unit was written");
    assert!(unit.contains("ExecStart=/usr/bin/node server.js"));
    assert!(unit.contains("Environment=PORT=3000"));
    assert!(unit.contains(&format!("User={}", input.account.as_str())));
}

#[test]
fn a_site_that_is_not_a_node_site_gets_no_unit() {
    let host = FakeSiteHost::passing();
    let input = php_input();
    let paths = SitePaths::for_site(&input.account, &input.domain, input.web_server);

    write_node_unit(&host, distro(), &input, &paths).unwrap();

    assert!(
        host.config(&node_unit_path(distro(), &input.domain))
            .is_none()
    );
}

#[test]
fn a_unit_that_already_says_what_it_should_is_not_written_again() {
    let host = FakeSiteHost::passing();
    let input = node_input(SiteWebServer::Nginx);
    let paths = SitePaths::for_site(&input.account, &input.domain, input.web_server);
    write_node_unit(&host, distro(), &input, &paths).unwrap();
    let before = host.config(&node_unit_path(distro(), &input.domain));

    write_node_unit(&host, distro(), &input, &paths).unwrap();

    assert_eq!(
        host.config(&node_unit_path(distro(), &input.domain)),
        before
    );
}
