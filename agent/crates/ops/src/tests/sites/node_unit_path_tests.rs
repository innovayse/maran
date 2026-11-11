//! Tests for the Node.js unit's name and the major it declares.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_templates::systemd::node_site_unit::NodeSiteUnit;

use super::{declared_node_major, is_node_unit_file_name};

#[test]
fn the_major_a_rendered_unit_declares_is_read_back() {
    // The template and the reader are one contract: a template change that moves the major
    // fails here and not on a host.
    let arguments = vec!["server.js".to_owned()];
    for major in ["18", "20", "22", "24"] {
        let unit = NodeSiteUnit {
            account: "acme",
            domain: "example.com",
            node_major: major,
            document_root: "/home/acme/sites/example.com",
            home_directory: "/home/acme",
            port: 3000,
            executable: "/usr/bin/node",
            arguments: &arguments,
        }
        .render_config()
        .unwrap();

        assert_eq!(declared_node_major(&unit), Some(major));
    }
}

#[test]
fn text_that_states_no_major_declares_none() {
    // Inverse controls: nothing is invented for text the reader cannot read.
    assert_eq!(declared_node_major(""), None);
    assert_eq!(
        declared_node_major("[Unit]\nDescription=Something else\n"),
        None
    );
    assert_eq!(
        declared_node_major("Description=Maran Node.js lts application for example.com\n"),
        None
    );
    assert_eq!(declared_node_major("Description=Maran Node.js \n"), None);
}

#[test]
fn only_the_per_site_node_units_are_node_units() {
    assert!(is_node_unit_file_name("maran-node-example.com.service"));
    assert!(!is_node_unit_file_name("maran-agent.service"));
    assert!(!is_node_unit_file_name("maran-node-example.com.timer"));
    assert!(!is_node_unit_file_name("nginx.service"));
}
