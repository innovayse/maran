//! Tests for the `write_node_unit` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::node_major::NodeMajor;
use maran_agent_core::validation::web::node_start_command::NodeStartCommand;
use maran_agent_core::validation::web::port::Port;
use maran_distro::{DistroAdapter, DistroFamily, SiteWebServer, adapter_for};

use super::write_node_unit;
use crate::sites::fake_site_host::{FakeSiteHost, distro, node_input, php_input};
use crate::sites::model::create_site_input::CreateSiteInput;
use crate::sites::model::site_kind::SiteKind;
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

/// The unit of a Node.js site declared on `major`, written on `adapter`'s family, for the command
/// `command`.
fn unit_for(adapter: &dyn DistroAdapter, major: &str, command: &str) -> String {
    let host = FakeSiteHost::passing();
    let input = CreateSiteInput {
        kind: SiteKind::Node {
            version: NodeMajor::parse(major).unwrap(),
            port: Port::parse(3000).unwrap(),
            start_command: NodeStartCommand::parse(command).unwrap(),
        },
        ..node_input(SiteWebServer::Nginx)
    };
    let paths = SitePaths::for_site(&input.account, &input.domain, input.web_server);

    write_node_unit(&host, adapter, &input, &paths).unwrap();

    host.config(&node_unit_path(adapter, &input.domain))
        .expect("the unit was written")
}

/// The `ExecStart=` line of `unit`.
fn exec_start(unit: &str) -> &str {
    unit.lines()
        .find(|line| line.starts_with("ExecStart="))
        .expect("the unit has an ExecStart")
}

#[test]
fn two_sites_on_different_majors_run_different_interpreters_on_a_per_major_family() {
    // Issue #530: the unit used to run the single /usr/bin/node whatever the site declared.
    let adapter = adapter_for(DistroFamily::Suse);
    let twenty = unit_for(adapter, "20", "node server.js");
    let twenty_two = unit_for(adapter, "22", "node server.js");

    assert_eq!(exec_start(&twenty), "ExecStart=/usr/bin/node20 server.js");
    assert_eq!(
        exec_start(&twenty_two),
        "ExecStart=/usr/bin/node22 server.js"
    );
}

#[test]
fn the_unit_runs_the_file_the_installed_probe_asks_about() {
    let adapter = adapter_for(DistroFamily::Suse);
    let major = NodeMajor::parse("22").unwrap();

    let unit = unit_for(adapter, "22", "node server.js");

    assert_eq!(
        exec_start(&unit),
        format!("ExecStart={} server.js", adapter.node_binary(&major))
    );
}

#[test]
fn an_npm_site_runs_the_npm_launcher_of_its_own_major() {
    let adapter = adapter_for(DistroFamily::Suse);

    let twenty = unit_for(adapter, "20", "npm start");
    let twenty_two = unit_for(adapter, "22", "npm start");

    assert_eq!(exec_start(&twenty), "ExecStart=/usr/bin/npm20 start");
    assert_eq!(exec_start(&twenty_two), "ExecStart=/usr/bin/npm22 start");
}

#[test]
fn the_families_with_one_interpreter_run_it_for_every_major() {
    // Inverse control: the Debian family has one `nodejs` package and the RHEL family one active
    // module stream, so the two majors share the path and the difference above comes from the
    // adapter's per-major fact and from nothing else.
    for family in [DistroFamily::Debian, DistroFamily::Rhel] {
        let adapter = adapter_for(family);

        let twenty = unit_for(adapter, "20", "node server.js");
        let twenty_two = unit_for(adapter, "22", "node server.js");

        assert_eq!(exec_start(&twenty), exec_start(&twenty_two));
        assert_eq!(exec_start(&twenty), "ExecStart=/usr/bin/node server.js");
    }
}
