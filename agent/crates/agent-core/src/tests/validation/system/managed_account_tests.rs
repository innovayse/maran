//! Tests for the `managed_account` module.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise (rules/testing.md). `managed_account.rs` declares this file
//! with `#[path]`, which keeps it a child module and therefore able to reach the
//! private reserved list and the floor-injecting constructors, so no test here
//! depends on the machine's `login.defs` or password database.
//!
//! Every refusal is paired with an accepting control on the same seam, so a
//! constructor that refuses everything cannot pass the suite.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::*;

/// Class A of the threat note: superuser and base system identities, 25 names.
const CLASS_A: &[&str] = &[
    "root",
    "daemon",
    "bin",
    "sys",
    "sync",
    "games",
    "man",
    "mail",
    "news",
    "uucp",
    "proxy",
    "backup",
    "list",
    "irc",
    "gnats",
    "nobody",
    "nogroup",
    "adm",
    "halt",
    "shutdown",
    "operator",
    "gopher",
    "ftp",
    "www",
    "nfsnobody",
];

/// Class B of the threat note: daemon identities of installed software, 44 names.
const CLASS_B: &[&str] = &[
    "nginx",
    "apache",
    "httpd",
    "sshd",
    "dbus",
    "polkitd",
    "rpc",
    "rpcuser",
    "chrony",
    "ntp",
    "tss",
    "tcpdump",
    "avahi",
    "colord",
    "sssd",
    "unbound",
    "named",
    "bind",
    "mysql",
    "mariadb",
    "postgres",
    "redis",
    "memcached",
    "mongodb",
    "mongod",
    "rabbitmq",
    "elasticsearch",
    "postfix",
    "dovecot",
    "dovenull",
    "exim",
    "opendkim",
    "clamav",
    "amavis",
    "spamd",
    "vsftpd",
    "saslauth",
    "mailnull",
    "smmsp",
    "squid",
    "haproxy",
    "varnish",
    "tomcat",
    "git",
];

/// Class C of the threat note: the panel's own exact name, 1 name.
const CLASS_C: &[&str] = &["maran"];

/// Class D of the threat note: groups and roles other software trusts, 15 names.
const CLASS_D: &[&str] = &[
    "sudo", "wheel", "admin", "staff", "users", "shadow", "disk", "tty", "utmp", "dialout",
    "cdrom", "docker", "lxd", "kvm", "libvirt",
];

/// Class E of the threat note: role mailbox names, 7 names.
const CLASS_E: &[&str] = &[
    "administrator",
    "hostmaster",
    "postmaster",
    "webmaster",
    "abuse",
    "security",
    "noc",
];

/// Class F of the threat note: default login users of cloud images, 11 names.
const CLASS_F: &[&str] = &[
    "ubuntu",
    "debian",
    "almalinux",
    "rocky",
    "centos",
    "fedora",
    "ec2_user",
    "cloud_user",
    "vagrant",
    "azureuser",
    "opc",
];

/// The floors the tests inject, deliberately far from the usual 1000 so a
/// hard-coded constant in the code under test is caught.
const UID_FLOOR: u32 = 60000;

/// The group floor the tests inject, different from [`UID_FLOOR`].
const GID_FLOOR: u32 = 61000;

/// Every name of the threat note's table, in one list.
fn every_noted_name() -> Vec<&'static str> {
    [CLASS_A, CLASS_B, CLASS_C, CLASS_D, CLASS_E, CLASS_F].concat()
}

/// Parses a fixture name that is known to be a valid spelling.
fn account_name(candidate: &str) -> AccountName {
    AccountName::parse(candidate).expect("fixture name is a valid spelling")
}

/// One passwd line in the file's seven-field shape.
fn passwd_line(name: &str, uid: u32, gid: u32, home: &str) -> String {
    format!("{name}:x:{uid}:{gid}:Fixture:{home}:/bin/bash\n")
}

/// The text of a database holding root and one account row, as a host would.
fn passwd_with(name: &str, uid: u32, gid: u32, home: &str) -> String {
    format!(
        "root:x:0:0:root:/root:/bin/bash\n{}",
        passwd_line(name, uid, gid, home)
    )
}

/// Resolves `acme`-style fixtures against the injected floors.
fn admit(name: &str, passwd: &str) -> Result<ManagedAccount, ManagedAccountError> {
    ManagedAccount::admit(account_name(name), passwd, UID_FLOOR, GID_FLOOR)
}

#[test]
fn the_reserved_list_is_exactly_the_threat_notes_table() {
    let mut expected = every_noted_name();
    let mut actual = RESERVED_NAMES.to_vec();
    expected.sort_unstable();
    actual.sort_unstable();

    assert_eq!(actual, expected);
}

#[test]
fn the_classes_of_the_table_have_their_noted_sizes_and_the_list_has_103_names() {
    assert_eq!(CLASS_A.len(), 25);
    assert_eq!(CLASS_B.len(), 44);
    assert_eq!(CLASS_C.len(), 1);
    assert_eq!(CLASS_D.len(), 15);
    assert_eq!(CLASS_E.len(), 7);
    assert_eq!(CLASS_F.len(), 11);
    assert_eq!(RESERVED_NAMES.len(), 103);
}

#[test]
fn the_reserved_list_has_no_duplicate() {
    let mut sorted = RESERVED_NAMES.to_vec();
    sorted.sort_unstable();
    sorted.dedup();

    assert_eq!(sorted.len(), RESERVED_NAMES.len());
}

#[test]
fn every_reserved_entry_is_itself_a_valid_account_name() {
    for entry in RESERVED_NAMES {
        assert!(
            AccountName::parse(entry).is_ok(),
            "{entry} is a dead entry: the alphabet already refuses it"
        );
    }
}

#[test]
fn the_panel_prefix_is_maran_underscore() {
    assert_eq!(PANEL_PREFIX, "maran_");
}

#[test]
fn every_reserved_name_is_refused() {
    for entry in every_noted_name() {
        assert_eq!(
            ManagedAccount::refuse_reserved(&account_name(entry)),
            Err(ManagedAccountError::Reserved),
            "{entry} must be reserved"
        );
    }
}

#[test]
fn every_name_starting_with_the_panel_prefix_is_refused() {
    for candidate in ["maran_api", "maran_x", "maran_ab_cd", "maran_42"] {
        assert_eq!(
            ManagedAccount::refuse_reserved(&account_name(candidate)),
            Err(ManagedAccountError::Reserved),
            "{candidate} starts with the panel prefix"
        );
    }
}

#[test]
fn a_name_that_only_begins_with_a_reserved_name_is_not_reserved() {
    // The comparison is equality, not a prefix match: a prefix match would turn
    // the list into a ban on every name that merely starts like a daemon.
    for candidate in [
        "rooted",
        "wwwsite",
        "mysqlfan",
        "nginx_shop",
        "maranacct",
        "marana",
        "rootx",
        "ubuntu1",
    ] {
        assert_eq!(
            ManagedAccount::refuse_reserved(&account_name(candidate)),
            Ok(()),
            "{candidate} is an ordinary name"
        );
    }
}

#[test]
fn a_name_that_merely_contains_a_reserved_name_is_not_reserved() {
    for candidate in ["my_root", "acme_mysql", "xxdaemon", "the_admin_x"] {
        assert_eq!(
            ManagedAccount::refuse_reserved(&account_name(candidate)),
            Ok(()),
            "{candidate} only contains a reserved name"
        );
    }
}

#[test]
fn an_ordinary_name_is_not_reserved() {
    assert_eq!(
        ManagedAccount::refuse_reserved(&account_name("acme")),
        Ok(())
    );
}

#[test]
fn an_ordinary_account_above_the_floors_with_its_own_home_is_managed() {
    let passwd = passwd_with("acme", UID_FLOOR, GID_FLOOR, "/home/acme");

    let managed = admit("acme", &passwd).unwrap();

    assert_eq!(managed.name().as_str(), "acme");
    assert_eq!(managed.uid(), UID_FLOOR);
    assert_eq!(managed.gid(), GID_FLOOR);
    assert_eq!(managed.home(), "/home/acme");
}

#[test]
fn the_name_of_a_managed_account_is_the_parsed_name_it_was_resolved_from() {
    let passwd = passwd_with("client_42", UID_FLOOR, GID_FLOOR, "/home/client_42");

    let managed = admit("client_42", &passwd).unwrap();

    assert_eq!(managed.name(), &account_name("client_42"));
}

#[test]
fn a_reserved_name_is_refused_even_when_its_row_is_perfect() {
    // `ubuntu` clears the floors and has its own home: only the list refuses it.
    let passwd = passwd_with("ubuntu", UID_FLOOR, GID_FLOOR, "/home/ubuntu");

    assert_eq!(admit("ubuntu", &passwd), Err(ManagedAccountError::Reserved));
}

#[test]
fn a_reserved_name_is_refused_before_the_database_is_consulted() {
    // No row at all: were the row looked up first the answer would be NotFound.
    // Reserved is the answer, so a reserved name cannot be used to ask whether
    // such a user exists.
    assert_eq!(
        admit("mysql", "root:x:0:0:root:/root:/bin/bash\n"),
        Err(ManagedAccountError::Reserved)
    );
    assert_eq!(admit("redis", ""), Err(ManagedAccountError::Reserved));
}

#[test]
fn a_reserved_name_is_refused_before_the_ids_and_the_home_are_judged() {
    // Root's row has uid 0 and home /root; the answer is still Reserved.
    assert_eq!(
        admit("root", "root:x:0:0:root:/root:/bin/bash\n"),
        Err(ManagedAccountError::Reserved)
    );
}

#[test]
fn a_prefixed_panel_name_is_refused_by_the_resolution_too() {
    let passwd = passwd_with("maran_api", UID_FLOOR, GID_FLOOR, "/home/maran_api");

    assert_eq!(
        admit("maran_api", &passwd),
        Err(ManagedAccountError::Reserved)
    );
}

#[test]
fn a_name_the_database_does_not_hold_is_not_found() {
    let passwd = passwd_with("other", UID_FLOOR, GID_FLOOR, "/home/other");

    assert_eq!(admit("acme", &passwd), Err(ManagedAccountError::NotFound));
}

#[test]
fn an_empty_database_holds_no_account() {
    assert_eq!(admit("acme", ""), Err(ManagedAccountError::NotFound));
}

#[test]
fn a_row_whose_name_only_begins_with_the_asked_name_is_not_that_account() {
    let passwd = passwd_with("acmex", UID_FLOOR, GID_FLOOR, "/home/acmex");

    assert_eq!(admit("acme", &passwd), Err(ManagedAccountError::NotFound));
}

#[test]
fn a_row_whose_name_only_ends_with_the_asked_name_is_not_that_account() {
    let passwd = passwd_with("xacme", UID_FLOOR, GID_FLOOR, "/home/xacme");

    assert_eq!(admit("acme", &passwd), Err(ManagedAccountError::NotFound));
}

#[test]
fn a_neighbouring_longer_name_does_not_make_the_account_ambiguous() {
    // The row count is by exact name. A count by prefix would see `acme` and
    // `acmex` as two rows of one name and refuse an account that is
    // unambiguous.
    let passwd = format!(
        "{}{}",
        passwd_line("acme", UID_FLOOR, GID_FLOOR, "/home/acme"),
        passwd_line("acmex", UID_FLOOR + 1, GID_FLOOR + 1, "/home/acmex"),
    );

    let account = admit("acme", &passwd).expect("the account is the only row of its name");

    assert_eq!(account.name().as_str(), "acme");
    assert_eq!(account.uid(), UID_FLOOR);
}

#[test]
fn two_rows_of_the_same_name_are_ambiguous_and_refused() {
    let passwd = format!(
        "{}{}",
        passwd_line("acme", UID_FLOOR, GID_FLOOR, "/home/acme"),
        passwd_line("acme", UID_FLOOR + 1, GID_FLOOR + 1, "/home/acme"),
    );

    assert_eq!(admit("acme", &passwd), Err(ManagedAccountError::NotFound));
}

#[test]
fn a_malformed_second_row_of_the_same_name_still_makes_the_answer_ambiguous() {
    // The parser drops the truncated line, so counting parsed rows would see one
    // and accept. The raw text shows two, and two is refused.
    let passwd = format!(
        "{}acme:x:oops\n",
        passwd_line("acme", UID_FLOOR, GID_FLOOR, "/home/acme"),
    );

    assert_eq!(admit("acme", &passwd), Err(ManagedAccountError::NotFound));
}

#[test]
fn a_row_that_cannot_be_read_as_a_passwd_line_is_absent_and_never_a_pass() {
    assert_eq!(
        admit(
            "acme",
            "acme:x:not_a_number:61000:Fixture:/home/acme:/bin/bash\n"
        ),
        Err(ManagedAccountError::NotFound)
    );
}

#[test]
fn a_commented_out_row_is_not_an_account() {
    let passwd = format!(
        "#{}",
        passwd_line("acme", UID_FLOOR, GID_FLOOR, "/home/acme")
    );

    assert_eq!(admit("acme", &passwd), Err(ManagedAccountError::NotFound));
}

#[test]
fn a_home_that_is_not_exactly_home_slash_name_is_refused() {
    for home in [
        "/root",
        "/var/lib/mysql",
        "/home/other",
        "/home/acmex",
        "/home/acme/",
        "/home/acme/sub",
        "/home//acme",
        "/home/../home/acme",
        "/srv/home/acme",
        "/Home/acme",
        "home/acme",
        "/",
        "",
    ] {
        let passwd = passwd_with("acme", UID_FLOOR, GID_FLOOR, home);

        assert_eq!(
            admit("acme", &passwd),
            Err(ManagedAccountError::WrongHome),
            "home {home:?} must be refused"
        );
    }
}

#[test]
fn the_expected_home_is_the_agents_account_home_root_joined_with_the_name() {
    let home = format!("{}/acme", AgentPaths::ACCOUNT_HOME_ROOT);
    let passwd = passwd_with("acme", UID_FLOOR, GID_FLOOR, &home);

    assert_eq!(admit("acme", &passwd).unwrap().home(), home);
}

#[test]
fn a_login_whose_home_is_a_jail_is_not_a_managed_account() {
    // An SFTP login parses as an account name and shares its account's ids,
    // but its home is a jail.
    let passwd = passwd_with("acme_sftp", UID_FLOOR, GID_FLOOR, "/srv/sftp/acme_sftp");

    assert_eq!(
        admit("acme_sftp", &passwd),
        Err(ManagedAccountError::WrongHome)
    );
}

#[test]
fn the_root_uid_is_refused() {
    let passwd = passwd_with("acme", 0, GID_FLOOR, "/home/acme");

    assert_eq!(
        admit("acme", &passwd),
        Err(ManagedAccountError::SystemIdentity)
    );
}

#[test]
fn a_root_primary_group_is_refused() {
    let passwd = passwd_with("acme", UID_FLOOR, 0, "/home/acme");

    assert_eq!(
        admit("acme", &passwd),
        Err(ManagedAccountError::SystemIdentity)
    );
}

#[test]
fn a_uid_one_below_the_hosts_floor_is_refused_and_the_floor_itself_is_accepted() {
    let below = passwd_with("acme", UID_FLOOR - 1, GID_FLOOR, "/home/acme");
    let at = passwd_with("acme", UID_FLOOR, GID_FLOOR, "/home/acme");

    assert_eq!(
        admit("acme", &below),
        Err(ManagedAccountError::SystemIdentity)
    );
    assert!(admit("acme", &at).is_ok());
}

#[test]
fn the_conventional_floor_of_1000_does_not_stand_in_for_the_hosts_floor() {
    // uid 1001 is above 1000 but below the injected floor: refused. A floor
    // hard-coded to 1000 would accept it.
    let passwd = passwd_with("acme", 1001, GID_FLOOR, "/home/acme");

    assert_eq!(
        admit("acme", &passwd),
        Err(ManagedAccountError::SystemIdentity)
    );
}

#[test]
fn a_gid_one_below_its_own_floor_is_refused_and_the_floor_itself_is_accepted() {
    let below = passwd_with("acme", UID_FLOOR, GID_FLOOR - 1, "/home/acme");
    let at = passwd_with("acme", UID_FLOOR, GID_FLOOR, "/home/acme");

    assert_eq!(
        admit("acme", &below),
        Err(ManagedAccountError::SystemIdentity)
    );
    assert!(admit("acme", &at).is_ok());
}

#[test]
fn a_gid_below_its_floor_is_refused_even_when_the_uid_clears_the_uid_floor() {
    // The gid is judged against the GID floor, not against the uid floor: here
    // the gid clears the lower uid floor of 1000 and still fails 2000.
    let passwd = passwd_with("acme", 5000, 1500, "/home/acme");

    assert_eq!(
        ManagedAccount::admit(account_name("acme"), &passwd, 1000, 2000),
        Err(ManagedAccountError::SystemIdentity)
    );
}

#[test]
fn a_gid_above_a_lower_gid_floor_is_accepted_even_when_it_is_below_the_uid_floor() {
    // The mirror of the case above: gid 1500 clears GID_MIN 1000 though it is
    // below UID_MIN 2000, so a gid judged against the uid floor would refuse it.
    let passwd = passwd_with("acme", 5000, 1500, "/home/acme");

    assert!(ManagedAccount::admit(account_name("acme"), &passwd, 2000, 1000).is_ok());
}

#[test]
fn the_home_is_judged_before_the_ids() {
    // Both wrong: the contract is reserved, row, home, then ids.
    let passwd = passwd_with("acme", 0, 0, "/root");

    assert_eq!(admit("acme", &passwd), Err(ManagedAccountError::WrongHome));
}

#[test]
fn the_row_is_judged_before_the_home() {
    let passwd = passwd_with("other", 0, 0, "/root");

    assert_eq!(admit("acme", &passwd), Err(ManagedAccountError::NotFound));
}

#[test]
fn rows_already_parsed_are_judged_by_the_same_rules() {
    let rows = system_accounts(&passwd_with("acme", UID_FLOOR, GID_FLOOR, "/home/acme"));

    let managed =
        ManagedAccount::admit_rows(account_name("acme"), &rows, UID_FLOOR, GID_FLOOR).unwrap();

    assert_eq!(managed.name().as_str(), "acme");
    assert_eq!(managed.home(), "/home/acme");
}

#[test]
fn parsed_rows_refuse_a_reserved_name_a_wrong_home_a_system_id_and_an_absent_row() {
    let reserved = system_accounts(&passwd_with("ubuntu", UID_FLOOR, GID_FLOOR, "/home/ubuntu"));
    let homeless = system_accounts(&passwd_with("acme", UID_FLOOR, GID_FLOOR, "/srv/acme"));
    let system = system_accounts(&passwd_with("acme", 5, GID_FLOOR, "/home/acme"));
    let absent = system_accounts(&passwd_with("other", UID_FLOOR, GID_FLOOR, "/home/other"));

    assert_eq!(
        ManagedAccount::admit_rows(account_name("ubuntu"), &reserved, UID_FLOOR, GID_FLOOR),
        Err(ManagedAccountError::Reserved)
    );
    assert_eq!(
        ManagedAccount::admit_rows(account_name("acme"), &homeless, UID_FLOOR, GID_FLOOR),
        Err(ManagedAccountError::WrongHome)
    );
    assert_eq!(
        ManagedAccount::admit_rows(account_name("acme"), &system, UID_FLOOR, GID_FLOOR),
        Err(ManagedAccountError::SystemIdentity)
    );
    assert_eq!(
        ManagedAccount::admit_rows(account_name("acme"), &absent, UID_FLOOR, GID_FLOOR),
        Err(ManagedAccountError::NotFound)
    );
}

#[test]
fn two_parsed_rows_of_the_same_name_are_ambiguous_and_refused() {
    let rows = vec![
        SystemAccount {
            name: "acme".to_owned(),
            uid: UID_FLOOR,
            gid: GID_FLOOR,
            home: "/home/acme".to_owned(),
        },
        SystemAccount {
            name: "acme".to_owned(),
            uid: UID_FLOOR + 1,
            gid: GID_FLOOR,
            home: "/home/acme".to_owned(),
        },
    ];

    assert_eq!(
        ManagedAccount::admit_rows(account_name("acme"), &rows, UID_FLOOR, GID_FLOOR),
        Err(ManagedAccountError::NotFound)
    );
}

#[test]
fn the_public_resolution_applies_the_hosts_own_floors() {
    // The machine's floors are unknown here, so only what holds for any floor is
    // asserted: root's row is never a managed account.
    assert!(
        ManagedAccount::resolve(
            account_name("acme"),
            &passwd_with("acme", 0, 0, "/home/acme")
        )
        .is_err()
    );
    assert_eq!(
        ManagedAccount::resolve(account_name("root"), "root:x:0:0:root:/root:/bin/bash\n"),
        Err(ManagedAccountError::Reserved)
    );
}

#[test]
fn the_public_row_resolution_refuses_what_every_floor_refuses() {
    let rows = system_accounts(&passwd_with("acme", 0, 0, "/home/acme"));

    assert_eq!(
        ManagedAccount::resolve_rows(account_name("acme"), &rows),
        Err(ManagedAccountError::SystemIdentity)
    );
}

#[test]
fn every_refusal_reason_is_its_own_variant() {
    let reasons = [
        ManagedAccountError::Reserved,
        ManagedAccountError::NotFound,
        ManagedAccountError::SystemIdentity,
        ManagedAccountError::WrongHome,
    ];

    assert!(matches!(reasons[0], ManagedAccountError::Reserved));
    assert!(matches!(reasons[1], ManagedAccountError::NotFound));
    assert!(matches!(reasons[2], ManagedAccountError::SystemIdentity));
    assert!(matches!(reasons[3], ManagedAccountError::WrongHome));
    for (index, reason) in reasons.iter().enumerate() {
        for other in &reasons[index + 1..] {
            assert_ne!(reason, other);
        }
    }
}

#[test]
fn no_refusal_message_carries_a_path_an_id_or_a_rule() {
    for reason in [
        ManagedAccountError::Reserved,
        ManagedAccountError::NotFound,
        ManagedAccountError::SystemIdentity,
        ManagedAccountError::WrongHome,
    ] {
        let message = reason.to_string();

        assert!(!message.contains('/'), "{message:?} carries a path");
        assert!(
            !message.chars().any(|c| c.is_ascii_digit()),
            "{message:?} carries an id"
        );
        assert!(!message.is_empty());
    }
}
