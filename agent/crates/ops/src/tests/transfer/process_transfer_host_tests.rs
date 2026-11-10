//! Tests for [`ProcessTransferHost`] against a temporary scratch area.
//!
//! The spawn and the system resolver are not driven (they are the bounded core's
//! and the operating system's); everything this host DECIDES is: the scratch
//! area must be root-only and the host's own, files are created exclusively at
//! mode `0600`, and nothing outside the area is written, read or removed.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs;
use std::net::IpAddr;
use std::os::unix::fs::{MetadataExt, PermissionsExt, symlink};
use std::path::PathBuf;

use maran_agent_core::validation::web::port::Port;
use maran_agent_core::validation::web::remote_host::RemoteHost;

use super::ProcessTransferHost;
use crate::transfer::transfer_host::TransferHost;
use crate::transfer::transfer_op_error::TransferOpError;

/// A host whose scratch area is `<temporary directory>/transfer`.
fn host_in(base: &tempfile::TempDir) -> (ProcessTransferHost, PathBuf) {
    let root = base.path().join("transfer");

    (ProcessTransferHost::at(root.clone()), root)
}

#[test]
fn a_scratch_directory_is_new_owner_only_and_directly_below_the_area() {
    let base = tempfile::tempdir().unwrap();
    let (host, root) = host_in(&base);

    let first = host.create_scratch().unwrap();
    let second = host.create_scratch().unwrap();

    assert_ne!(first, second, "a scratch directory was reused");
    for scratch in [&first, &second] {
        assert_eq!(scratch.parent(), Some(root.as_path()));
        let metadata = fs::symlink_metadata(scratch).unwrap();
        assert!(metadata.is_dir());
        assert_eq!(metadata.mode() & 0o777, 0o700);
    }
}

#[test]
fn a_scratch_area_others_can_enter_is_refused() {
    let base = tempfile::tempdir().unwrap();
    let (host, root) = host_in(&base);
    fs::create_dir(&root).unwrap();
    fs::set_permissions(&root, fs::Permissions::from_mode(0o755)).unwrap();

    assert!(matches!(
        host.create_scratch(),
        Err(TransferOpError::ScratchUnavailable)
    ));

    // Inverse control: the same area, closed, is accepted.
    fs::set_permissions(&root, fs::Permissions::from_mode(0o700)).unwrap();
    assert!(host.create_scratch().is_ok());
}

#[test]
fn a_scratch_area_that_is_a_link_is_refused() {
    let base = tempfile::tempdir().unwrap();
    let (host, root) = host_in(&base);
    let elsewhere = base.path().join("elsewhere");
    fs::create_dir(&elsewhere).unwrap();
    fs::set_permissions(&elsewhere, fs::Permissions::from_mode(0o700)).unwrap();
    symlink(&elsewhere, &root).unwrap();

    assert!(matches!(
        host.create_scratch(),
        Err(TransferOpError::ScratchUnavailable)
    ));
}

#[test]
fn a_private_file_is_written_at_mode_0600_and_never_overwritten() {
    let base = tempfile::tempdir().unwrap();
    let (host, _) = host_in(&base);
    let scratch = host.create_scratch().unwrap();
    let file = scratch.join("key");

    host.write_private_file(&file, b"first second\n").unwrap();

    assert_eq!(fs::read_to_string(&file).unwrap(), "first second\n");
    assert_eq!(fs::metadata(&file).unwrap().mode() & 0o777, 0o600);
    assert!(matches!(
        host.write_private_file(&file, b"replacement"),
        Err(TransferOpError::ScratchUnavailable)
    ));
    assert_eq!(fs::read_to_string(&file).unwrap(), "first second\n");
}

#[test]
fn a_file_outside_the_area_is_neither_written_nor_read() {
    let base = tempfile::tempdir().unwrap();
    let (host, root) = host_in(&base);
    let scratch = host.create_scratch().unwrap();
    let outside = base.path().join("outside");
    let climbing = scratch.join("..").join("..").join("outside");

    for path in [&outside, &climbing] {
        assert!(host.write_private_file(path, b"x").is_err());
        assert!(!outside.exists(), "a file was created outside the area");
    }
    fs::write(&outside, "visible").unwrap();
    assert!(host.read_small_file(&outside, 100).is_err());

    // Inverse control: inside the area the same calls succeed.
    let inside = root.join(scratch.file_name().unwrap()).join("note");
    host.write_private_file(&inside, b"seen").unwrap();
    assert_eq!(host.read_small_file(&inside, 100).unwrap(), "seen");
}

#[test]
fn a_file_over_the_limit_is_refused_and_one_at_the_limit_is_read() {
    let base = tempfile::tempdir().unwrap();
    let (host, _) = host_in(&base);
    let scratch = host.create_scratch().unwrap();
    let file = scratch.join("key");
    host.write_private_file(&file, b"abcdef").unwrap();

    assert_eq!(host.read_small_file(&file, 6).unwrap(), "abcdef");
    assert!(matches!(
        host.read_small_file(&file, 5),
        Err(TransferOpError::ScratchUnavailable)
    ));
}

#[test]
fn removing_a_scratch_directory_removes_its_files_and_is_repeatable() {
    let base = tempfile::tempdir().unwrap();
    let (host, _) = host_in(&base);
    let scratch = host.create_scratch().unwrap();
    host.write_private_file(&scratch.join("key"), b"secret")
        .unwrap();

    host.remove_scratch(&scratch).unwrap();
    assert!(!scratch.exists());

    host.remove_scratch(&scratch).unwrap();
}

#[test]
fn only_a_direct_child_of_the_area_can_be_removed() {
    let base = tempfile::tempdir().unwrap();
    let (host, root) = host_in(&base);
    let scratch = host.create_scratch().unwrap();
    let foreign = base.path().join("foreign");
    fs::create_dir(&foreign).unwrap();
    fs::write(foreign.join("keep"), "x").unwrap();

    assert!(host.remove_scratch(&foreign).is_err());
    assert!(host.remove_scratch(&root).is_err());
    assert!(host.remove_scratch(&scratch.join("..")).is_err());
    assert!(foreign.join("keep").exists());
    assert!(root.exists());

    // Positive control: a direct child goes.
    host.remove_scratch(&scratch).unwrap();
    assert!(!scratch.exists());
}

#[test]
fn a_link_in_the_area_is_not_followed_when_removing() {
    let base = tempfile::tempdir().unwrap();
    let (host, root) = host_in(&base);
    host.create_scratch().unwrap();
    let target = base.path().join("target");
    fs::create_dir(&target).unwrap();
    fs::write(target.join("keep"), "x").unwrap();
    let link = root.join("link");
    symlink(&target, &link).unwrap();

    assert!(host.remove_scratch(&link).is_err());
    assert!(
        target.join("keep").exists(),
        "the link's target was removed"
    );
}

#[test]
fn an_address_literal_resolves_to_itself_without_a_lookup() {
    let host = ProcessTransferHost::at(PathBuf::from("/nonexistent"));
    let literal = RemoteHost::parse("192.0.2.10").unwrap();

    let addresses = host.resolve(&literal, Port::parse(22).unwrap()).unwrap();

    assert_eq!(addresses, vec!["192.0.2.10".parse::<IpAddr>().unwrap()]);
}

#[test]
fn this_hosts_own_addresses_include_the_loopback() {
    let host = ProcessTransferHost::at(PathBuf::from("/nonexistent"));

    let addresses = host.local_addresses().unwrap();

    assert!(
        addresses.contains(&"127.0.0.1".parse::<IpAddr>().unwrap()),
        "the kernel table was not read: {addresses:?}"
    );
}
