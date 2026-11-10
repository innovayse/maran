//! Tests for the `vhost_server_names` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::vhost_server_names;

#[test]
fn an_nginx_server_name_line_yields_every_name() {
    let names = vhost_server_names("server {\n    server_name a.example b.example;\n}\n");

    assert_eq!(names, ["a.example", "b.example"]);
}

#[test]
fn apache_name_and_alias_lines_yield_every_name() {
    let names = vhost_server_names(
        "<VirtualHost *:80>\n    ServerName a.example\n    ServerAlias b.example\n    ServerAlias c.example\n</VirtualHost>\n",
    );

    assert_eq!(names, ["a.example", "b.example", "c.example"]);
}

#[test]
fn a_line_without_the_agents_indentation_is_not_read() {
    assert!(vhost_server_names("ServerName a.example\nserver_name b.example;\n").is_empty());
}
