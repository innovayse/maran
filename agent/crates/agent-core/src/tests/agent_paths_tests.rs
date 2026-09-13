//! Tests for the `agent_paths` module.
//!
//! The constants need no test — they are their own statement. The cron helpers
//! do: they compose three paths from an account name and an entry id, and the
//! whole point of composing them in one place is that the three cannot drift
//! apart.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use super::AgentPaths;
use crate::validation::system::backup_id::BackupId;
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

/// A parsed backup id for the backup helpers under test.
fn backup() -> BackupId {
    BackupId::parse("7c9e6679-7425-40de-944b-e07fc1f90ae7").unwrap()
}

#[test]
fn a_backups_artifact_and_sidecar_share_the_accounts_own_backup_directory() {
    let account = account();
    let backup = backup();
    let id = backup.as_str();

    let directory = AgentPaths::account_backup_dir(&account);
    assert_eq!(directory, Path::new("/var/backups/maran/acme"));

    let artifact = AgentPaths::backup_artifact_path(&account, &backup);
    let sidecar = AgentPaths::backup_sidecar_path(&account, &backup);

    assert_eq!(artifact, directory.join(format!("{id}.tar.gz")));
    assert_eq!(sidecar, directory.join(format!("{id}.meta.json")));
    for path in [&artifact, &sidecar] {
        assert_eq!(path.parent(), Some(directory.as_path()));
        assert!(!path.starts_with(AgentPaths::ACCOUNT_HOME_ROOT));
    }
}

#[test]
fn database_dumps_are_staged_where_only_root_can_reach_them() {
    // A dump sitting in account-writable space can be replaced between being
    // written and being loaded, and the loader connects as the database
    // superuser.
    let scratch = AgentPaths::backup_scratch_dir(&backup());

    assert!(scratch.starts_with(AgentPaths::BULK_SCRATCH_ROOT));
    assert!(!scratch.starts_with(AgentPaths::ACCOUNT_HOME_ROOT));
    assert!(scratch.ends_with(backup().as_str()));
}

#[test]
fn database_dumps_are_staged_on_disk_and_never_on_the_tmpfs_under_run() {
    // The defect this pins: /run is a tmpfs sized at a fraction of RAM, so a
    // dump staged there is resident kernel memory on a live root-run server,
    // and a restore holds one archive dump AND one rollback dump per database
    // at once. Moving the staging back under the /run scratch would be a
    // one-word change that nothing else in this workspace would object to.
    let scratch = AgentPaths::backup_scratch_dir(&backup());

    assert!(!scratch.starts_with(AgentPaths::agent_scratch_dir()));
    assert!(!scratch.starts_with("/run"));
    assert!(Path::new(AgentPaths::BULK_SCRATCH_ROOT).is_absolute());
}

#[test]
fn the_bulk_scratch_is_outside_every_directory_the_panel_uid_owns() {
    // The escalation this pins, measured in
    // docs/superpowers/notes/2026-09-05-backups-threat-note.md §1: the scratch
    // used to be /var/lib/maran/scratch, and /var/lib/maran is created
    // maran:maran 0750 by installer/lib/40-user.sh. The panel uid owned the
    // parent, so it could rename the leaf aside and leave a symlink at that
    // name — which needs write permission on the parent only — and root's next
    // dump write followed it. Two measured outcomes: a customer's plaintext
    // dump landing in a panel-readable file, and a root-owned 0600 file
    // truncated and overwritten. The 0700 modes on the leaves were real and
    // stopped neither, because the attacker never had to enter them.
    assert!(!Path::new(AgentPaths::BULK_SCRATCH_ROOT).starts_with("/var/lib/maran/"));
    assert_ne!(
        Path::new(AgentPaths::BULK_SCRATCH_ROOT),
        Path::new("/var/lib/maran")
    );

    // The inverse control on the same axis: the assertions above are also
    // satisfied by a scratch somewhere useless (or by /home, which the panel
    // cannot write but every account can). It is still under /var/lib, whose
    // own mode is root:root 0755 on both families.
    assert!(Path::new(AgentPaths::BULK_SCRATCH_ROOT).starts_with("/var/lib"));
}

#[test]
fn the_small_and_the_bulk_scratch_are_two_different_places() {
    // The inverse control for the test above: it is satisfied by the two
    // constants being anything at all as long as one is not under the other,
    // including by the bulk root having quietly become the /run one under a
    // second name. They are distinct roots, and the small one is still the
    // /run one, because the crontab staging that depends on being reboot-clean
    // still lives there.
    assert_eq!(
        AgentPaths::agent_scratch_dir(),
        Path::new("/run/maran/scratch")
    );
    assert_ne!(
        Path::new(AgentPaths::BULK_SCRATCH_ROOT),
        AgentPaths::agent_scratch_dir()
    );
}

#[test]
fn the_two_restore_paths_share_one_parent_so_the_swap_is_a_rename() {
    // The swap is `home -> previous` then `staging -> home`. A rename is only
    // atomic within one filesystem, so both staging paths sit under one root
    // that is itself on the same filesystem as the homes. Moving either of them
    // elsewhere turns the swap into a copy and nothing else in the code would
    // object.
    let account = account();
    let backup = backup();

    let staging = AgentPaths::restore_staging_dir(&account, &backup);
    let previous = AgentPaths::restore_previous_dir(&account, &backup);

    assert_eq!(staging.parent(), previous.parent());
    assert_eq!(
        staging.parent(),
        Some(Path::new(AgentPaths::RESTORE_STAGING_ROOT))
    );
    assert!(Path::new(AgentPaths::RESTORE_STAGING_ROOT).starts_with(AgentPaths::ACCOUNT_HOME_ROOT));
    assert_ne!(staging, previous);

    let id = backup.as_str();
    assert_eq!(
        staging,
        Path::new("/home/.maran-restore").join(format!("acme.{id}"))
    );
    assert_eq!(
        previous,
        Path::new("/home/.maran-restore").join(format!("acme.previous.{id}"))
    );
}

#[test]
fn the_ftps_jail_root_is_not_inside_the_panel_owned_state_root() {
    // The escalation that made every SFTP login on every real install fail, and
    // that this constant is placed to avoid repeating: /var/lib/maran is
    // created maran:maran 0750 by installer/lib/40-user.sh, so an unprivileged
    // uid owning an ancestor of a chroot can rename a level aside and leave an
    // entry of its own at the name every customer's jail hangs under — without
    // ever having permission to enter it. It is also what OpenSSH refuses
    // outright, walking every component of the chroot path.
    assert!(!Path::new(AgentPaths::FTPS_JAIL_ROOT).starts_with("/var/lib/maran/"));
    assert_ne!(
        Path::new(AgentPaths::FTPS_JAIL_ROOT),
        Path::new("/var/lib/maran")
    );
    assert_eq!(AgentPaths::FTPS_JAIL_ROOT, "/var/lib/maran-ftps");

    // The inverse control on the same axis: the refusals above are satisfied by
    // a jail root anywhere at all, /home included — which every account can
    // write. It is under /var/lib, whose own mode is root:root 0755 on both
    // families, so every component above the base belongs to root.
    assert!(Path::new(AgentPaths::FTPS_JAIL_ROOT).starts_with("/var/lib"));
}

#[test]
fn the_two_jail_roots_are_separate_directories_and_neither_contains_the_other() {
    // The two protocols' jails have different lifetimes: an account may hold
    // logins of one and none of the other, and removing the last login of one
    // must not unmount the other's bind mount out from under a live customer.
    // Nesting one root inside the other would make a recursive teardown of
    // either reach the other's mounts.
    assert_ne!(AgentPaths::FTPS_JAIL_ROOT, AgentPaths::SFTP_JAIL_ROOT);
    assert!(!Path::new(AgentPaths::FTPS_JAIL_ROOT).starts_with(AgentPaths::SFTP_JAIL_ROOT));
    assert!(!Path::new(AgentPaths::SFTP_JAIL_ROOT).starts_with(AgentPaths::FTPS_JAIL_ROOT));
}
