//! Tests for `system_accounts`, the local password database parser.

// A failing assertion IS the reporting mechanism for a test, so the workspace-wide
// bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::system_accounts;

/// A few rows in the shape both supported families ship, including the two
/// kinds of row this repository actually distinguishes: a hosting account under
/// the home root, and an SFTP login whose home is its account's jail.
const PASSWD: &str = "root:x:0:0:root:/root:/bin/bash\n\
                      daemon:x:1:1:daemon:/usr/sbin:/usr/sbin/nologin\n\
                      alice:x:1001:1001::/home/alice:/usr/sbin/nologin\n\
                      alice_deploy:x:1001:1001::/var/lib/maran/sftp/alice:/usr/sbin/nologin\n";

#[test]
fn every_row_of_the_database_becomes_an_account() {
    let accounts = system_accounts(PASSWD);

    assert_eq!(accounts.len(), 4);
    assert_eq!(accounts[0].name, "root");
    assert_eq!(accounts[2].name, "alice");
}

#[test]
fn the_four_fields_are_read_from_their_own_positions() {
    let accounts = system_accounts(PASSWD);

    assert_eq!(accounts[2].name, "alice");
    assert_eq!(accounts[2].uid, 1001);
    assert_eq!(accounts[2].gid, 1001);
    assert_eq!(accounts[2].home, "/home/alice");
}

#[test]
fn an_empty_gecos_field_does_not_shift_the_home() {
    // The home is the sixth field counted from the front. Counting from the end
    // instead would read the shell here, because gecos is empty on every account
    // this agent creates.
    let accounts = system_accounts("alice:x:1001:1001::/home/alice:/usr/sbin/nologin\n");

    assert_eq!(accounts[0].home, "/home/alice");
}

#[test]
fn a_row_with_no_login_shell_is_still_an_account() {
    let accounts = system_accounts("alice:x:1001:1001::/home/alice:\n");

    assert_eq!(accounts.len(), 1);
    assert_eq!(accounts[0].home, "/home/alice");
}

#[test]
fn a_malformed_row_is_skipped_and_the_rest_are_read() {
    let accounts = system_accounts(
        "# a comment\n\
         \n\
         +@sysadmins\n\
         broken:x:not-a-number:1001::/home/broken:/bin/sh\n\
         alice:x:1001:1001::/home/alice:/usr/sbin/nologin\n",
    );

    assert_eq!(accounts.len(), 1);
    assert_eq!(accounts[0].name, "alice");
}

#[test]
fn a_truncated_row_is_not_an_account() {
    // A partial write leaves a line with fewer fields than a home to read.
    let accounts = system_accounts("alice:x:1001:1001\n");

    assert!(accounts.is_empty());
}

#[test]
fn a_row_whose_uid_is_not_a_number_is_not_an_account() {
    // Stricter than the method body this was extracted from, which never looked
    // at the numeric fields. Pinned here so the difference stays a decision:
    // every caller acts on the account a row describes, and a row `useradd`
    // could not have written is not one to act on.
    assert!(system_accounts("bob:x:oops:1001::/home/bob:/bin/sh\n").is_empty());
    assert!(system_accounts("bob:x:-1:1001::/home/bob:/bin/sh\n").is_empty());
    assert!(system_accounts("bob:x:4294967296:1001::/home/bob:/bin/sh\n").is_empty());
}

#[test]
fn a_row_whose_gid_is_not_a_number_is_not_an_account() {
    assert!(system_accounts("bob:x:1001:oops::/home/bob:/bin/sh\n").is_empty());
}

#[test]
fn a_home_containing_the_separator_truncates_at_it() {
    // Not a nicety: it is what the extracted method body did, byte for byte,
    // and the SFTP area compares this value against a jail path. A row whose
    // home holds a colon is a row no tool on the host can round-trip either.
    let accounts = system_accounts("bob:x:1001:1001::/home/we:ird:/bin/sh\n");

    assert_eq!(accounts[0].home, "/home/we");
}

#[test]
fn a_row_with_no_name_is_not_an_account() {
    let accounts = system_accounts(":x:1001:1001::/home/alice:/bin/sh\n");

    assert!(accounts.is_empty());
}

#[test]
fn an_empty_database_yields_no_accounts() {
    assert!(system_accounts("").is_empty());
}

#[test]
fn the_rows_come_back_in_the_files_own_order() {
    let accounts = system_accounts(PASSWD);
    let names: Vec<&str> = accounts
        .iter()
        .map(|account| account.name.as_str())
        .collect();

    assert_eq!(names, ["root", "daemon", "alice", "alice_deploy"]);
}
