//! Reads the answer of the database-server probe.

use crate::transfer::model::source_database_server::SourceDatabaseServer;

/// The server `text` (the banner of `mysql --version` or its siblings) names:
/// MariaDB when the first line says so, MySQL when it says `mysql`, else `None`.
///
/// MariaDB is asked first because its client banner begins with the word
/// `mysql` too.
#[must_use]
pub fn parse_database_server(text: &str) -> Option<SourceDatabaseServer> {
    let banner = text.lines().next()?.to_ascii_lowercase();

    if banner.contains("mariadb") {
        Some(SourceDatabaseServer::Mariadb)
    } else if banner.contains("mysql") {
        Some(SourceDatabaseServer::Mysql)
    } else {
        None
    }
}

#[cfg(test)]
#[path = "../../tests/transfer/probes/parse_database_server_tests.rs"]
mod tests;
