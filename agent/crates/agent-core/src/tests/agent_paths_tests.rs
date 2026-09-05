//! Tests for the `agent_paths` module.
//!
//! The constants need no test — they are their own statement. The cron helpers
//! do: they compose three paths from an account name and an entry id, and the
//! whole point of composing them in one place is that the three cannot drift
//! apart.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use super::AgentPaths;
use crate::validation::system::cron_entry_id::CronEntryId;
use crate::validation::system::name::AccountName;

/// A parsed account name for the helpers under test.
fn account() -> AccountName {
    AccountName::parse("acme").unwrap()
}

/// A parsed entry id for the helpers under test.
fn entry() -> CronEntryId {
    CronEntryId::parse("3f2a1c04-9d5b-4f0e-8a3d-71b2c6e0d4aa").unwrap()
}

#[test]
fn the_cron_directory_sits_inside_the_accounts_own_home() {
    assert_eq!(
        AgentPaths::account_cron_dir(&account()),
        Path::new("/home/acme/.maran/cron")
    );
}

#[test]
fn an_entrys_three_run_files_share_one_directory_and_differ_only_in_extension() {
    let account = account();
    let entry = entry();
    let id = entry.as_str();

    let command = AgentPaths::cron_cmd_path(&account, &entry);
    let log = AgentPaths::cron_log_path(&account, &entry);
    let exit = AgentPaths::cron_exit_path(&account, &entry);

    let directory = AgentPaths::account_cron_dir(&account);
    for path in [&command, &log, &exit] {
        assert_eq!(path.parent(), Some(directory.as_path()));
    }

    assert_eq!(command, directory.join(format!("{id}.cmd")));
    assert_eq!(log, directory.join(format!("{id}.log")));
    assert_eq!(exit, directory.join(format!("{id}.exit")));
}

#[test]
fn no_entry_id_that_parses_can_move_a_run_file_out_of_the_cron_directory() {
    // The defence is the type, not a check in this file: `Path::join` with an
    // absolute string replaces the path, and `..` climbs out of it, so the id
    // is refused before a path is built rather than sanitised after. This test
    // states the pairing — the hostile spellings do not parse, and everything
    // that does parse stays put.
    let account = account();
    let directory = AgentPaths::account_cron_dir(&account);

    for hostile in ["/etc/cron.d/evil", "../../etc/passwd", "", ".."] {
        assert!(
            CronEntryId::parse(hostile).is_err(),
            "`{hostile}` must never reach a path helper"
        );
    }

    for accepted in [
        "3f2a1c04-9d5b-4f0e-8a3d-71b2c6e0d4aa",
        "00000000-0000-0000-0000-000000000000",
        "ffffffff-ffff-ffff-ffff-ffffffffffff",
    ] {
        let id = CronEntryId::parse(accepted).unwrap();

        for path in [
            AgentPaths::cron_cmd_path(&account, &id),
            AgentPaths::cron_log_path(&account, &id),
            AgentPaths::cron_exit_path(&account, &id),
        ] {
            assert_eq!(path.parent(), Some(directory.as_path()));
            assert!(path.starts_with(&directory));
            assert!(!path.to_string_lossy().contains(".."));
        }
    }
}

#[test]
fn two_accounts_never_share_a_cron_directory() {
    let one = AccountName::parse("acme").unwrap();
    let other = AccountName::parse("acme2").unwrap();

    assert_ne!(
        AgentPaths::account_cron_dir(&one),
        AgentPaths::account_cron_dir(&other)
    );
}

#[test]
fn the_agent_writes_its_own_temporary_files_outside_every_account_home() {
    // A root-written temporary file anywhere an account can reach is a symlink
    // an account can pre-plant, which is why the root-side crontab file is
    // written here and not under the home it is installed for.
    let scratch = AgentPaths::agent_scratch_dir();

    assert!(scratch.is_absolute());
    assert!(!scratch.starts_with(AgentPaths::ACCOUNT_HOME_ROOT));
}

#[test]
fn the_two_nftables_files_are_distinct_absolute_paths() {
    // Distinct because the ruleset file is replaced whole on every apply and a
    // ban living in it would be erased by every rule change.
    let ruleset = AgentPaths::nftables_ruleset_path();
    let bans = AgentPaths::nftables_bans_path();

    assert_ne!(ruleset, bans);
    assert!(ruleset.is_absolute());
    assert!(bans.is_absolute());
}
