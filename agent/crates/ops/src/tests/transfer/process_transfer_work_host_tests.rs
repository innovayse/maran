//! Tests for [`ProcessTransferWorkHost`] against temporary directories.
//!
//! The fork as an account needs root and is not driven here; everything this host
//! DECIDES without a privilege is: dump files are new, owner-only and removable
//! only by the name this host gave them, a removal outside the area is refused,
//! a refused room check writes nothing.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs;
use std::os::unix::fs::{MetadataExt, PermissionsExt};
use std::path::PathBuf;

use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;

use super::ProcessTransferWorkHost;
use crate::transfer::transfer_op_error::TransferOpError;
use crate::transfer::transfer_work_host::TransferWorkHost;

/// A host whose areas are below a temporary directory, with a floor of 1 MiB.
fn host_in(base: &tempfile::TempDir) -> (ProcessTransferWorkHost, PathBuf, PathBuf) {
    let receivers = base.path().join("receivers");
    let dumps = base.path().join("dumps");
    let host = ProcessTransferWorkHost::at(
        receivers.clone(),
        dumps.clone(),
        FreeSpaceFloor::from_mebibytes(1).unwrap(),
    );

    (host, receivers, dumps)
}

#[test]
fn a_dump_file_is_new_owner_only_and_directly_below_the_area() {
    let base = tempfile::tempdir().unwrap();
    let (host, _, dumps) = host_in(&base);

    let first = host.create_dump_file(10).unwrap();
    let second = host.create_dump_file(10).unwrap();

    assert_ne!(first.path, second.path);
    for dump in [&first, &second] {
        assert_eq!(dump.path.parent(), Some(dumps.as_path()));
        assert_eq!(fs::metadata(&dump.path).unwrap().mode() & 0o777, 0o600);
    }
    assert_eq!(fs::metadata(&dumps).unwrap().mode() & 0o777, 0o700);
}

#[test]
fn a_dump_file_the_disk_has_no_room_for_is_refused_and_nothing_is_created() {
    let base = tempfile::tempdir().unwrap();
    let (host, _, dumps) = host_in(&base);

    let refused = host.create_dump_file(u64::MAX / 2);

    assert!(matches!(refused, Err(TransferOpError::DiskInsufficient)));
    // Inverse control: a small one is accepted on the same host.
    assert!(host.create_dump_file(10).is_ok());
    assert_eq!(fs::read_dir(&dumps).unwrap().count(), 1);
}

#[test]
fn a_dump_area_others_can_enter_is_refused() {
    let base = tempfile::tempdir().unwrap();
    let (host, _, dumps) = host_in(&base);
    fs::create_dir(&dumps).unwrap();
    fs::set_permissions(&dumps, fs::Permissions::from_mode(0o755)).unwrap();

    assert!(matches!(
        host.create_dump_file(10),
        Err(TransferOpError::ScratchUnavailable)
    ));
}

#[test]
fn only_a_file_this_host_named_in_its_area_is_removed() {
    let base = tempfile::tempdir().unwrap();
    let (host, _, dumps) = host_in(&base);
    let dump = host.create_dump_file(10).unwrap();
    let stranger = base.path().join("dump-elsewhere.sql");
    fs::write(&stranger, "x").unwrap();
    let unnamed = dumps.join("notes.txt");
    fs::write(&unnamed, "x").unwrap();

    assert!(host.remove_dump_file(&stranger).is_err());
    assert!(host.remove_dump_file(&unnamed).is_err());
    assert!(stranger.exists() && unnamed.exists());
    // Inverse control: its own file goes, and removing it twice is success.
    host.remove_dump_file(&dump.path).unwrap();
    host.remove_dump_file(&dump.path).unwrap();
    assert!(!dump.path.exists());
}

#[test]
fn a_receiver_directory_outside_the_area_is_never_removed() {
    let base = tempfile::tempdir().unwrap();
    let (host, receivers, _) = host_in(&base);
    fs::create_dir_all(&receivers).unwrap();
    let stranger = base.path().join("elsewhere");
    fs::create_dir(&stranger).unwrap();

    assert!(host.remove_receiver_directory(&stranger).is_err());
    assert!(stranger.exists());
    // Inverse control: a directory directly in the area is removed, and removing it
    // again is success.
    let own = receivers.join("abc123");
    fs::create_dir(&own).unwrap();
    host.remove_receiver_directory(&own).unwrap();
    host.remove_receiver_directory(&own).unwrap();
    assert!(!own.exists());
}
