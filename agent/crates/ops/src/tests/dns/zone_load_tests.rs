//! Which `rndc` command makes the daemon serve a zone's new file.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::ZoneLoad;
use crate::dns::model::signing_mode::SigningMode;
use crate::dns::model::zone_reading::ZoneReading;

fn served() -> ZoneReading {
    ZoneReading::Served { serial: 1 }
}

#[test]
fn a_zone_the_daemon_does_not_know_is_added_whatever_it_is_to_become() {
    for wanted in [
        SigningMode::Unsigned,
        SigningMode::Signed,
        SigningMode::Withdrawing,
    ] {
        assert_eq!(
            ZoneLoad::choose(&ZoneReading::NotServed, None, wanted),
            ZoneLoad::Add
        );
    }
}

#[test]
fn a_known_zone_whose_mode_changes_is_modified() {
    // The planted change, once per direction that matters.
    assert_eq!(
        ZoneLoad::choose(&served(), Some(SigningMode::Unsigned), SigningMode::Signed),
        ZoneLoad::Modify
    );
    assert_eq!(
        ZoneLoad::choose(&served(), Some(SigningMode::Signed), SigningMode::Unsigned),
        ZoneLoad::Modify
    );
    assert_eq!(
        ZoneLoad::choose(
            &served(),
            Some(SigningMode::Signed),
            SigningMode::Withdrawing
        ),
        ZoneLoad::Modify
    );
}

#[test]
fn a_known_zone_whose_mode_is_unchanged_is_only_reloaded() {
    // The inverse control: nothing changed, so the statement is left alone.
    for mode in [
        SigningMode::Unsigned,
        SigningMode::Signed,
        SigningMode::Withdrawing,
    ] {
        assert_eq!(
            ZoneLoad::choose(&served(), Some(mode), mode),
            ZoneLoad::Reload
        );
    }
}

#[test]
fn a_known_zone_whose_mode_could_not_be_read_is_modified_and_never_reloaded() {
    assert_eq!(
        ZoneLoad::choose(&served(), None, SigningMode::Unsigned),
        ZoneLoad::Modify
    );
}

#[test]
fn the_command_carries_a_statement_for_add_and_modify_and_none_for_reload() {
    assert_eq!(
        ZoneLoad::Add.command("z.test", "{ s };"),
        ["addzone", "z.test", "{ s };"]
    );
    assert_eq!(
        ZoneLoad::Modify.command("z.test", "{ s };"),
        ["modzone", "z.test", "{ s };"]
    );
    assert_eq!(
        ZoneLoad::Reload.command("z.test", "{ s };"),
        ["reload", "z.test"]
    );
}
