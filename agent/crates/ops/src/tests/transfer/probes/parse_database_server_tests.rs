//! Tests for [`parse_database_server`].

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::parse_database_server;
use crate::transfer::model::source_database_server::SourceDatabaseServer;

#[test]
fn mariadb_banners_are_mariadb_even_when_they_begin_with_mysql() {
    for banner in [
        "mysql from 11.4.2-MariaDB, client 15.2 for debian-linux-gnu (x86_64) using  EditLine wrapper\n",
        "mariadb from 10.11.6-MariaDB, client 15.2 for Linux (x86_64)\n",
        "mysqld  Ver 10.5.22-MariaDB for Linux on x86_64 (MariaDB Server)\n",
    ] {
        assert_eq!(
            parse_database_server(banner),
            Some(SourceDatabaseServer::Mariadb),
            "{banner}"
        );
    }
}

#[test]
fn a_mysql_banner_is_mysql() {
    assert_eq!(
        parse_database_server(
            "mysql  Ver 8.0.36-0ubuntu0.22.04.1 for Linux on x86_64 ((Ubuntu))\n"
        ),
        Some(SourceDatabaseServer::Mysql)
    );
}

#[test]
fn text_that_names_neither_is_unknown() {
    for banner in ["", "\n", "postgres (PostgreSQL) 16.2\n"] {
        assert_eq!(parse_database_server(banner), None, "{banner:?}");
    }
}

#[test]
fn only_the_first_line_decides() {
    assert_eq!(parse_database_server("something\nMariaDB\n"), None);
}
