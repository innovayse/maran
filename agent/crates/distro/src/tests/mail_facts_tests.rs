//! Tests for the mail facts of both family adapters.
//!
//! The literals are the record of what `research_notes/mail-stack-inventory.md` found, written
//! out and not read back from the adapters, so changing a unit name or a package is an edit
//! here as well. A cell the inventory marks unverified is unverified in these tests too: they
//! pin the CHOICE made, not a measurement of a host.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::mail::mail_password_scheme::MailPasswordScheme;

use crate::debian::DebianAdapter;
use crate::rhel::RhelAdapter;
use crate::{DovecotMajor, KeyValueServer, MailFacts};

/// Every release the Debian family's mail facts name, with the Dovecot generation it ships.
const DEBIAN_RELEASES: [(&str, DovecotMajor); 4] = [
    ("12", DovecotMajor::V23),
    ("13", DovecotMajor::V24),
    ("22.04", DovecotMajor::V23),
    ("24.04", DovecotMajor::V23),
];

/// Debian 13 is the one supported release whose Dovecot is 2.4, and every other listed
/// release of the family is 2.3.
#[test]
fn debian_13_alone_ships_the_second_dovecot_generation() {
    for (release, expected) in DEBIAN_RELEASES {
        assert_eq!(
            DebianAdapter.dovecot_major(release),
            Some(expected),
            "{release}"
        );
    }
}

/// A Debian-family release nobody has read the Dovecot of has no answer, and so does a
/// release string that is not one.
#[test]
fn an_unknown_debian_family_release_has_no_dovecot_generation() {
    assert_eq!(DebianAdapter.dovecot_major("14"), None);
    assert_eq!(DebianAdapter.dovecot_major("26.04"), None);
    assert_eq!(DebianAdapter.dovecot_major(""), None);
    assert_eq!(DebianAdapter.dovecot_major("13.1"), None);
}

/// The RHEL family ships Dovecot 2.3 on 9 and 10, whatever minor `VERSION_ID` carries.
#[test]
fn the_rhel_family_ships_the_first_dovecot_generation_on_every_minor() {
    for release in ["9", "9.4", "9.7", "10", "10.0"] {
        assert_eq!(
            RhelAdapter.dovecot_major(release),
            Some(DovecotMajor::V23),
            "{release}"
        );
    }
}

/// A RHEL-family release outside 9 and 10 has no answer, and a prefix match on the digits
/// does not make `19` a `9`.
#[test]
fn an_unknown_rhel_family_release_has_no_dovecot_generation() {
    assert_eq!(RhelAdapter.dovecot_major("8.10"), None);
    assert_eq!(RhelAdapter.dovecot_major("11"), None);
    assert_eq!(RhelAdapter.dovecot_major("19"), None);
    assert_eq!(RhelAdapter.dovecot_major(""), None);
}

/// The Debian family runs Redis on every release it lists.
#[test]
fn the_debian_family_runs_redis_on_every_listed_release() {
    let expected = KeyValueServer {
        package: "redis-server",
        service: "redis-server",
        user: "redis",
        config_file: "/etc/redis/redis.conf",
    };

    for (release, _) in DEBIAN_RELEASES {
        assert_eq!(
            DebianAdapter.key_value_server(release),
            Some(expected),
            "{release}"
        );
    }
    assert_eq!(DebianAdapter.key_value_server("14"), None);
}

/// The RHEL family runs Redis on 9 and Valkey on 10, each under its own package, unit,
/// user and configuration file.
#[test]
fn the_rhel_family_runs_redis_on_nine_and_valkey_on_ten() {
    let redis = RhelAdapter.key_value_server("9.4").unwrap();
    let valkey = RhelAdapter.key_value_server("10.0").unwrap();

    assert_eq!(
        (redis.package, redis.service, redis.user, redis.config_file),
        ("redis", "redis", "redis", "/etc/redis/redis.conf")
    );
    assert_eq!(
        (
            valkey.package,
            valkey.service,
            valkey.user,
            valkey.config_file
        ),
        ("valkey", "valkey", "valkey", "/etc/valkey/valkey.conf")
    );
    assert_eq!(RhelAdapter.key_value_server("8.10"), None);
}

/// The Debian family hashes mailbox passwords with Argon2id and the RHEL family, whose
/// Dovecot is built without libsodium, with bcrypt.
#[test]
fn the_two_families_name_the_password_scheme_their_dovecot_is_built_for() {
    assert_eq!(
        DebianAdapter.mail_password_scheme(),
        MailPasswordScheme::Argon2id
    );
    assert_eq!(
        RhelAdapter.mail_password_scheme(),
        MailPasswordScheme::BlowfishCrypt
    );
}

/// Both families write Postfix maps as `lmdb`, the one type RHEL 10's Postfix still has,
/// and list the package that provides it.
#[test]
fn both_families_write_postfix_maps_as_lmdb_and_install_its_package() {
    assert_eq!(DebianAdapter.postfix_map_type(), "lmdb");
    assert_eq!(RhelAdapter.postfix_map_type(), "lmdb");
    assert!(DebianAdapter.mail_packages().contains(&"postfix-lmdb"));
    assert!(RhelAdapter.mail_packages().contains(&"postfix-lmdb"));
}

/// The two families split the stack into different packages, and each list names its
/// own family's Dovecot and Sieve packages.
#[test]
fn each_family_lists_its_own_dovecot_and_sieve_packages() {
    assert_eq!(
        DebianAdapter.mail_packages(),
        [
            "postfix",
            "postfix-lmdb",
            "dovecot-core",
            "dovecot-imapd",
            "dovecot-pop3d",
            "dovecot-lmtpd",
            "dovecot-sieve",
            "rspamd",
        ]
    );
    assert_eq!(
        RhelAdapter.mail_packages(),
        [
            "postfix",
            "postfix-lmdb",
            "dovecot",
            "dovecot-pigeonhole",
            "rspamd"
        ]
    );
}

/// ClamAV is not in the main package list on either family: it is installed only when the
/// administrator turns the antivirus on.
#[test]
fn clamav_is_never_in_the_default_mail_package_list() {
    for packages in [DebianAdapter.mail_packages(), RhelAdapter.mail_packages()] {
        assert!(!packages.iter().any(|name| name.contains("clam")));
        assert!(
            !packages
                .iter()
                .any(|name| name.contains("redis") || name.contains("valkey"))
        );
    }
    assert_eq!(
        DebianAdapter.antivirus_packages(),
        ["clamav", "clamav-daemon", "clamav-freshclam"]
    );
    assert_eq!(
        RhelAdapter.antivirus_packages(),
        ["clamav", "clamd", "clamav-update"]
    );
}

/// The units the families agree on, and the two ClamAV spellings they do not.
#[test]
fn the_families_agree_on_the_mail_units_and_differ_on_the_antivirus_one() {
    for adapter in [
        &DebianAdapter as &dyn MailFacts,
        &RhelAdapter as &dyn MailFacts,
    ] {
        assert_eq!(adapter.postfix_service(), "postfix");
        assert_eq!(adapter.dovecot_service(), "dovecot");
        assert_eq!(adapter.rspamd_service(), "rspamd");
        assert_eq!(adapter.freshclam_service(), "clamav-freshclam");
    }
    assert_eq!(DebianAdapter.clamd_service(), "clamav-daemon");
    assert_eq!(RhelAdapter.clamd_service(), "clamd@scan");
}

/// The service users, with the ClamAV daemon's the one that differs.
#[test]
fn the_service_users_are_the_ones_the_packages_create() {
    for adapter in [
        &DebianAdapter as &dyn MailFacts,
        &RhelAdapter as &dyn MailFacts,
    ] {
        assert_eq!(adapter.postfix_user(), "postfix");
        assert_eq!(adapter.postdrop_group(), "postdrop");
        assert_eq!(adapter.dovecot_user(), "dovecot");
        assert_eq!(adapter.dovecot_login_user(), "dovenull");
        assert_eq!(adapter.rspamd_user(), "_rspamd");
    }
    assert_eq!(DebianAdapter.clamd_user(), "clamav");
    assert_eq!(RhelAdapter.clamd_user(), "clamscan");
}

/// The configuration directories are the packages' own, and every one is absolute.
#[test]
fn the_configuration_paths_are_the_packages_own_absolute_paths() {
    for adapter in [
        &DebianAdapter as &dyn MailFacts,
        &RhelAdapter as &dyn MailFacts,
    ] {
        assert_eq!(adapter.postfix_config_directory(), "/etc/postfix");
        assert_eq!(adapter.dovecot_config_directory(), "/etc/dovecot");
        assert_eq!(adapter.rspamd_config_directory(), "/etc/rspamd");
        assert!(adapter.clamd_config_file().starts_with('/'));
    }
    assert_eq!(DebianAdapter.clamd_config_file(), "/etc/clamav/clamd.conf");
    assert_eq!(RhelAdapter.clamd_config_file(), "/etc/clamd.d/scan.conf");
}

/// The Postfix tools live in `/usr/sbin` and the Dovecot and Rspamd ones in `/usr/bin`.
#[test]
fn the_postfix_tools_are_in_sbin_and_the_dovecot_and_rspamd_tools_in_bin() {
    for adapter in [
        &DebianAdapter as &dyn MailFacts,
        &RhelAdapter as &dyn MailFacts,
    ] {
        assert_eq!(adapter.postfix_binary(), "/usr/sbin/postfix");
        assert_eq!(adapter.postconf_binary(), "/usr/sbin/postconf");
        assert_eq!(adapter.postmap_binary(), "/usr/sbin/postmap");
        assert_eq!(adapter.postqueue_binary(), "/usr/sbin/postqueue");
        assert_eq!(adapter.postsuper_binary(), "/usr/sbin/postsuper");
        assert_eq!(adapter.doveadm_binary(), "/usr/bin/doveadm");
        assert_eq!(adapter.doveconf_binary(), "/usr/bin/doveconf");
        assert_eq!(adapter.rspamadm_binary(), "/usr/bin/rspamadm");
    }
}
