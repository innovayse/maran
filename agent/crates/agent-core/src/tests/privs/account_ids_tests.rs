//! What can be checked about id resolution without being root.
//!
//! The fork itself is not exercised here. Dropping to a real account needs a real
//! account and a root process, so it is covered by the container test in Task 11,
//! where the agent runs as root against an account it created. What is asserted
//! below is the half that runs unprivileged: the lookup answers correctly, and it
//! refuses rather than panics.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::*;

use std::process::Command;

/// The name of the account this test process is running as, if it has one.
///
/// Read from the environment rather than from a second `getpwuid` binding, so the
/// test does not verify the code under test with the code under test.
fn current_username() -> Option<String> {
    let output = Command::new("/usr/bin/id").arg("-un").output().ok()?;
    if !output.status.success() {
        return None;
    }
    let name = String::from_utf8(output.stdout).ok()?;
    let name = name.trim().to_owned();
    if name.is_empty() { None } else { Some(name) }
}

#[test]
fn unknown_account_is_refused_rather_than_panicking() {
    let name = AccountName::parse("nosuchaccount_maran").expect("fixture name is valid");

    assert_eq!(AccountIds::resolve(&name), Err(PrivError::NoSuchAccount));
}

#[test]
fn a_name_that_is_not_an_account_at_all_is_refused() {
    // `zzz` is a syntactically valid account name that no system creates.
    let name = AccountName::parse("zzz").expect("fixture name is valid");

    assert_eq!(AccountIds::resolve(&name), Err(PrivError::NoSuchAccount));
}

#[test]
fn resolving_the_current_user_returns_this_processs_own_ids() {
    let Some(username) = current_username() else {
        // No usable `id`; nothing to compare against, so nothing to assert.
        return;
    };
    let Ok(name) = AccountName::parse(&username) else {
        // The account this test runs as is outside Maran's name alphabet (a CI
        // runner called `runner-1`, say). Not a failure of the code under test.
        return;
    };

    let resolved = AccountIds::resolve(&name);

    if username == "root" {
        // Running the suite as root is not the expected way to run it, but the
        // refusal is exactly what should happen, so it is asserted rather than
        // skipped.
        assert_eq!(resolved, Err(PrivError::RootAccount));
        return;
    }

    let own_uid = crate::utils::current_uid::current_uid().expect("/proc must be mounted");
    let (uid_minimum, _) = account_id_floors();
    if own_uid < uid_minimum {
        // A build agent running as a service identity below the host's UID_MIN.
        // The refusal is the correct answer, so it is asserted rather than skipped.
        assert_eq!(resolved, Err(PrivError::SystemAccount));
        return;
    }

    let ids = resolved.expect("the account this process runs as must resolve");
    assert_eq!(ids.uid(), own_uid);
    assert_ne!(ids.uid(), 0, "a resolved account is never root");
}

#[test]
fn the_root_account_is_refused_even_though_it_exists() {
    let name = AccountName::parse("root").expect("fixture name is valid");

    assert_eq!(AccountIds::resolve(&name), Err(PrivError::RootAccount));
}

#[test]
fn an_account_whose_primary_group_is_root_is_refused() {
    // gid 0 with a non-root uid is the hole the uid check alone does not close:
    // the child would drop, verify successfully, and run in root's group.
    assert_eq!(
        is_hosting_account(1000, 0, 1000, 1000),
        Err(PrivError::RootGroup)
    );
}

#[test]
fn the_root_uid_is_refused_before_the_group_is_considered() {
    // Named separately from the floor so an operator reading the log can tell
    // "you asked me to run as root" from "you asked me to run as nginx".
    assert_eq!(
        is_hosting_account(0, 0, 1000, 1000),
        Err(PrivError::RootAccount)
    );
}

#[test]
fn a_system_accounts_uid_is_below_the_floor_and_is_refused() {
    // `daemon` is 1:1 on both supported families, and `daemon` is a name
    // `AccountName::parse` accepts.
    assert_eq!(
        is_hosting_account(1, 1, 1000, 1000),
        Err(PrivError::SystemAccount)
    );
}

#[test]
fn a_system_accounts_gid_is_refused_even_when_its_uid_is_not() {
    // Both ids are checked, not just the uid: an account placed above the floor
    // but given a service group is still a lateral move onto that service.
    assert_eq!(
        is_hosting_account(1000, 33, 1000, 1000),
        Err(PrivError::SystemAccount)
    );
}

#[test]
fn the_first_hosting_account_is_accepted() {
    // The floor is inclusive: UID_MIN itself is the first human account.
    assert_eq!(is_hosting_account(1000, 1000, 1000, 1000), Ok(()));
}

#[test]
fn a_real_system_account_does_not_resolve_to_ids_a_child_could_drop_to() {
    // The end-to-end half of the checks above, against the machine's own user
    // database. `daemon` exists on every supported family; if it somehow does
    // not, `NoSuchAccount` is an equally correct refusal. What must never happen
    // is `Ok`.
    let name = AccountName::parse("daemon").expect("fixture name is valid");

    let resolved = AccountIds::resolve(&name);

    assert!(
        matches!(
            resolved,
            Err(PrivError::SystemAccount)
                | Err(PrivError::RootGroup)
                | Err(PrivError::NoSuchAccount)
        ),
        "a system account must never resolve: got {resolved:?}"
    );
}

#[test]
fn the_hosts_own_floors_never_admit_a_service_account() {
    // Reads this machine's real /etc/login.defs, so it can only ever exercise a
    // well-formed file. The hostile cases are covered against synthetic input
    // below; this one guards the integration with the real host.
    let (uid_minimum, gid_minimum) = account_id_floors();

    for floor in [uid_minimum, gid_minimum] {
        assert!(floor > 0, "a floor of 0 would disable the check entirely");
        assert!(
            floor >= 100,
            "a floor below 100 would admit the low service ids every distribution uses"
        );
    }
}

#[test]
fn an_explicit_zero_floor_is_refused_rather_than_trusted() {
    // THE case this parser exists to get right. `"0".parse::<u32>()` succeeds, so
    // a naive parser accepts it; the floor then becomes 0, `id < 0` is never true
    // for an unsigned integer, and every SystemAccount refusal silently stops
    // firing. One line in a config file and the protection is gone.
    assert_eq!(id_floor("UID_MIN 0\n", "UID_MIN"), FALLBACK_MINIMUM_ID);
}

#[test]
fn a_zero_floor_would_have_admitted_every_system_account() {
    // Spelled out so the consequence of the bug above is visible in the suite and
    // not only in a comment: with a floor of zero, `daemon` passes.
    assert_eq!(is_hosting_account(1, 1, 0, 0), Ok(()));
    // With the parser's answer for the same input, it does not.
    let floor = id_floor("UID_MIN 0\n", "UID_MIN");
    assert_eq!(
        is_hosting_account(1, 1, floor, floor),
        Err(PrivError::SystemAccount)
    );
}

#[test]
fn a_key_with_no_value_falls_back() {
    assert_eq!(id_floor("UID_MIN\n", "UID_MIN"), FALLBACK_MINIMUM_ID);
}

#[test]
fn a_non_numeric_value_falls_back() {
    assert_eq!(id_floor("UID_MIN banana\n", "UID_MIN"), FALLBACK_MINIMUM_ID);
}

#[test]
fn a_negative_value_falls_back() {
    // Parsed as u32, so the minus sign fails the parse rather than wrapping.
    assert_eq!(id_floor("UID_MIN -1\n", "UID_MIN"), FALLBACK_MINIMUM_ID);
}

#[test]
fn a_sys_prefixed_key_does_not_answer_for_the_real_one() {
    // SYS_UID_MIN is the floor for SYSTEM accounts — the exact ids this module
    // refuses. Letting it answer would invert the check.
    assert_eq!(
        id_floor("SYS_UID_MIN 100\n", "UID_MIN"),
        FALLBACK_MINIMUM_ID
    );
}

#[test]
fn a_commented_out_key_does_not_answer() {
    assert_eq!(
        id_floor("# UID_MIN 500\n#UID_MIN 500\n", "UID_MIN"),
        FALLBACK_MINIMUM_ID
    );
}

#[test]
fn the_first_usable_value_wins_over_a_duplicate_key() {
    assert_eq!(id_floor("UID_MIN 2000\nUID_MIN 3000\n", "UID_MIN"), 2000);
}

#[test]
fn an_unusable_first_value_falls_through_to_a_usable_duplicate() {
    // `find_map` skips entries that do not yield a floor, so a zero or a typo
    // ahead of a real value does not shadow it.
    assert_eq!(id_floor("UID_MIN 0\nUID_MIN 2000\n", "UID_MIN"), 2000);
}

#[test]
fn tabs_and_carriage_returns_around_the_value_are_tolerated() {
    // Debian ships this line tab-separated, and a file edited on Windows carries
    // CRLF. `split_whitespace` handles both, and `\r` is whitespace to it.
    assert_eq!(id_floor("UID_MIN\t\t\t 1500\r\n", "UID_MIN"), 1500);
}

#[test]
fn an_empty_file_gives_the_fallback() {
    // Which is also the answer for a file that could not be read at all.
    assert_eq!(id_floor("", "UID_MIN"), FALLBACK_MINIMUM_ID);
}

#[test]
fn the_group_floor_is_read_from_gid_min_and_not_from_uid_min() {
    // login.defs defines them separately. On a host where an administrator raised
    // GID_MIN above UID_MIN, substituting one for the other would accept a group
    // the host itself considers a system group.
    let contents = "UID_MIN 1000\nGID_MIN 5000\n";

    assert_eq!(id_floor(contents, "UID_MIN"), 1000);
    assert_eq!(id_floor(contents, "GID_MIN"), 5000);
}

#[test]
fn a_gid_between_the_two_floors_is_refused() {
    // The permissive failure that substituting UID_MIN for GID_MIN would cause:
    // gid 3000 clears a uid floor of 1000 and does not clear a gid floor of 5000.
    assert_eq!(
        is_hosting_account(1500, 3000, 1000, 5000),
        Err(PrivError::SystemAccount)
    );
}

#[test]
fn a_missing_gid_min_falls_back_without_borrowing_the_uid_floor() {
    // A host that sets only UID_MIN gets 1000 for the group floor, not 4000.
    let contents = "UID_MIN 4000\n";

    assert_eq!(id_floor(contents, "GID_MIN"), FALLBACK_MINIMUM_ID);
}
