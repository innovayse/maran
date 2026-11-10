//! What the probes established about a source onto its wire payload.

use maran_ops::transfer::model::source_database_server::SourceDatabaseServer;
use maran_ops::transfer::model::source_kind::SourceKind;
use maran_ops::transfer::model::source_web_server::SourceWebServer;
use maran_ops::transfer::model::tested_source::TestedSource;

use crate::proto;

/// The wire form of a tested source.
///
/// An observation that could not be made is the wire's unknown value: empty text,
/// the unspecified enum member, and zero disk figures with `disk_known` false.
/// A free figure of zero never means "unknown" on the wire, because the flag
/// beside it says which one it is.
#[must_use]
pub fn to_wire_tested_source(tested: &TestedSource) -> proto::TestedSource {
    let (free, total) = tested
        .home_disk
        .map_or((0, 0), |disk| (disk.free_bytes, disk.total_bytes));

    proto::TestedSource {
        is_root: tested.is_root,
        home_free_bytes: free,
        home_total_bytes: total,
        disk_known: tested.home_disk.is_some(),
        system_release: tested.system_release.clone().unwrap_or_default(),
        kind: match tested.kind {
            SourceKind::GenericLinux => proto::SourceKind::GenericLinux,
            SourceKind::Maran => proto::SourceKind::Maran,
        } as i32,
        maran_version: tested.maran_version.clone().unwrap_or_default(),
        database_server: match tested.database_server {
            None => proto::SourceDatabaseServer::Unspecified,
            Some(SourceDatabaseServer::Mariadb) => proto::SourceDatabaseServer::Mariadb,
            Some(SourceDatabaseServer::Mysql) => proto::SourceDatabaseServer::Mysql,
        } as i32,
        web_server: match tested.web_server {
            None => proto::SourceWebServer::Unspecified,
            Some(SourceWebServer::Nginx) => proto::SourceWebServer::Nginx,
            Some(SourceWebServer::Apache) => proto::SourceWebServer::Apache,
        } as i32,
        mail_found: tested.mail_found,
    }
}

#[cfg(test)]
#[path = "../../tests/services/transfer/to_wire_tested_source_tests.rs"]
mod tests;
