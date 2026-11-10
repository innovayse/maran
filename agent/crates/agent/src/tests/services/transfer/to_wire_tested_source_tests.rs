//! Tests for a tested source on the wire.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::transfer::{
    SourceDatabaseServer, SourceDisk, SourceKind, SourceWebServer, TestedSource,
};

use super::to_wire_tested_source;
use crate::proto;

/// A source every probe answered about.
fn complete() -> TestedSource {
    TestedSource {
        is_root: true,
        home_disk: Some(SourceDisk {
            free_bytes: 10,
            total_bytes: 100,
        }),
        system_release: Some("Debian GNU/Linux 12".to_owned()),
        kind: SourceKind::Maran,
        maran_version: Some("1.2.3".to_owned()),
        database_server: Some(SourceDatabaseServer::Mariadb),
        web_server: Some(SourceWebServer::Nginx),
        mail_found: true,
    }
}

/// A source every probe failed to read.
fn unread() -> TestedSource {
    TestedSource {
        is_root: false,
        home_disk: None,
        system_release: None,
        kind: SourceKind::GenericLinux,
        maran_version: None,
        database_server: None,
        web_server: None,
        mail_found: false,
    }
}

#[test]
fn every_observation_that_was_made_is_carried() {
    let wire = to_wire_tested_source(&complete());

    assert!(wire.is_root);
    assert!(wire.disk_known);
    assert_eq!((wire.home_free_bytes, wire.home_total_bytes), (10, 100));
    assert_eq!(wire.system_release, "Debian GNU/Linux 12");
    assert_eq!(wire.kind, proto::SourceKind::Maran as i32);
    assert_eq!(wire.maran_version, "1.2.3");
    assert_eq!(
        wire.database_server,
        proto::SourceDatabaseServer::Mariadb as i32
    );
    assert_eq!(wire.web_server, proto::SourceWebServer::Nginx as i32);
    assert!(wire.mail_found);
}

#[test]
fn an_observation_that_could_not_be_made_is_the_wires_unknown_value() {
    let wire = to_wire_tested_source(&unread());

    assert!(!wire.is_root);
    assert!(
        !wire.disk_known,
        "zero figures must not read as a measurement"
    );
    assert_eq!((wire.home_free_bytes, wire.home_total_bytes), (0, 0));
    assert!(wire.system_release.is_empty());
    assert_eq!(wire.kind, proto::SourceKind::GenericLinux as i32);
    assert!(wire.maran_version.is_empty());
    assert_eq!(
        wire.database_server,
        proto::SourceDatabaseServer::Unspecified as i32
    );
    assert_eq!(wire.web_server, proto::SourceWebServer::Unspecified as i32);
    assert!(!wire.mail_found);
}

#[test]
fn each_database_and_web_server_maps_to_its_own_member() {
    for (server, expected) in [
        (
            SourceDatabaseServer::Mariadb,
            proto::SourceDatabaseServer::Mariadb,
        ),
        (
            SourceDatabaseServer::Mysql,
            proto::SourceDatabaseServer::Mysql,
        ),
    ] {
        let wire = to_wire_tested_source(&TestedSource {
            database_server: Some(server),
            ..unread()
        });
        assert_eq!(wire.database_server, expected as i32);
    }
    for (server, expected) in [
        (SourceWebServer::Nginx, proto::SourceWebServer::Nginx),
        (SourceWebServer::Apache, proto::SourceWebServer::Apache),
    ] {
        let wire = to_wire_tested_source(&TestedSource {
            web_server: Some(server),
            ..unread()
        });
        assert_eq!(wire.web_server, expected as i32);
    }
}
