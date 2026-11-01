//! The real challenge host against a temporary directory.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::*;

#[test]
fn a_challenge_is_written_at_0644_and_removed_and_a_second_removal_is_not_found() {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join("token");
    let host = ProcessPanelChallengeHost::new();

    host.write_challenge(&path, "token.abc").unwrap();

    assert_eq!(std::fs::read_to_string(&path).unwrap(), "token.abc");
    let mode = std::fs::metadata(&path).unwrap().permissions().mode() & 0o777;
    assert_eq!(mode, 0o644);

    host.remove_challenge(&path).unwrap();
    assert!(!path.exists());
    assert!(matches!(
        host.remove_challenge(&path),
        Err(SslOpError::ChallengeNotFound)
    ));
}

#[test]
fn a_link_at_the_target_name_is_replaced_and_never_written_through() {
    let directory = tempfile::tempdir().unwrap();
    let outside = directory.path().join("outside");
    std::fs::write(&outside, "untouched").unwrap();
    let path = directory.path().join("token");
    std::os::unix::fs::symlink(&outside, &path).unwrap();

    ProcessPanelChallengeHost::new()
        .write_challenge(&path, "token.abc")
        .unwrap();

    assert_eq!(std::fs::read_to_string(&outside).unwrap(), "untouched");
    assert!(
        !std::fs::symlink_metadata(&path)
            .unwrap()
            .file_type()
            .is_symlink()
    );
}

#[test]
fn a_write_into_a_missing_directory_is_a_challenge_write_failure() {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join("missing").join("token");

    let refused = ProcessPanelChallengeHost::new().write_challenge(&path, "token.abc");

    assert!(matches!(refused, Err(SslOpError::ChallengeWrite { .. })));
}
