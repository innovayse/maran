//! The seven exit statuses a child can end with stay distinct bytes.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{
    EXIT_DROP_SETGID_FAILED, EXIT_DROP_SETGROUPS_FAILED, EXIT_DROP_SETUID_FAILED,
    EXIT_NO_SUCH_ENTRY, EXIT_OK, EXIT_VERIFICATION_FAILED, EXIT_WORK_FAILED,
};

/// The control the three above need: that they are three statuses and not one.
///
/// Without it, three constants that happened to share a value would make all three tests pass
/// while the agent reported "setgroups" for every failure — which is the defect, dressed as a
/// fix. The statuses are also checked against the other exit codes, since a collision with
/// EXIT_WORK_FAILED would mis-report work as a drop.
#[test]
fn the_three_drop_statuses_are_distinct_from_each_other_and_from_the_rest() {
    let statuses = [
        EXIT_OK,
        EXIT_DROP_SETGROUPS_FAILED,
        EXIT_DROP_SETGID_FAILED,
        EXIT_DROP_SETUID_FAILED,
        EXIT_VERIFICATION_FAILED,
        EXIT_WORK_FAILED,
        EXIT_NO_SUCH_ENTRY,
    ];

    for (index, status) in statuses.iter().enumerate() {
        assert!(
            !statuses[index + 1..].contains(status),
            "exit status {status} is used for more than one outcome"
        );

        // An exit status is one byte: a constant above 255 would be truncated by the kernel and
        // arrive as a different outcome entirely.
        assert!(
            (0..=255).contains(status),
            "exit status {status} is not a byte"
        );
    }
}
