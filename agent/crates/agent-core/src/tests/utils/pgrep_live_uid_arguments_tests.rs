//! Tests for [`pgrep_live_uid_arguments`], the probe that ignores zombies.

use crate::utils::pgrep_live_uid_arguments::pgrep_live_uid_arguments;

#[test]
fn the_arguments_count_live_processes_of_the_real_uid() {
    assert_eq!(
        pgrep_live_uid_arguments(1001),
        [
            "--count",
            "--runstates",
            "D,R,S,T,t,W,X,I,P",
            "--uid",
            "1001"
        ]
    );
}

#[test]
fn the_zombie_state_is_never_in_the_list_of_states() {
    let arguments = pgrep_live_uid_arguments(4242);

    assert!(
        !arguments[2].split(',').any(|state| state == "Z"),
        "{arguments:?}"
    );
}
