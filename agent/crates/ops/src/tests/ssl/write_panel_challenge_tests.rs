//! What `write_panel_challenge` writes, and when the floor refuses it.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use maran_agent_core::utils::floor_refusal::FloorRefusal;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_agent_core::validation::web::acme_key_authorization::AcmeKeyAuthorization;
use maran_agent_core::validation::web::acme_token::AcmeToken;

use super::*;
use crate::ssl::fake_panel_challenge_host::FakePanelChallengeHost;

const MIB: u64 = 1024 * 1024;

fn token() -> AcmeToken {
    AcmeToken::parse("evaGxfADs6pSRb2LAv9IZf17Dt3juxGJ").unwrap()
}

fn key_authorization() -> AcmeKeyAuthorization {
    AcmeKeyAuthorization::parse(&token(), "evaGxfADs6pSRb2LAv9IZf17Dt3juxGJ.thumb-print_1").unwrap()
}

fn floor() -> FreeSpaceFloor {
    FreeSpaceFloor::from_mebibytes(1).unwrap()
}

#[test]
fn the_key_authorization_lands_in_the_panel_challenge_directory_named_by_the_token() {
    let host = FakePanelChallengeHost::with_room(10 * MIB);

    write_panel_challenge(&host, &floor(), &token(), &key_authorization()).unwrap();

    let files = host.files.lock().unwrap();
    assert_eq!(
        files.get(Path::new(
            "/var/lib/maran-acme/evaGxfADs6pSRb2LAv9IZf17Dt3juxGJ"
        )),
        Some(&"evaGxfADs6pSRb2LAv9IZf17Dt3juxGJ.thumb-print_1".to_owned())
    );
    assert_eq!(
        host.measured.lock().unwrap().as_slice(),
        [Path::new("/var/lib/maran-acme").to_path_buf()]
    );
}

#[test]
fn a_challenge_below_the_free_space_floor_is_refused_before_anything_is_written() {
    let host = FakePanelChallengeHost::with_room(MIB - 1);

    let refused = write_panel_challenge(&host, &floor(), &token(), &key_authorization());

    assert!(matches!(
        refused,
        Err(SslOpError::FreeSpace(FloorRefusal::BelowFloor { .. }))
    ));
    assert!(host.files.lock().unwrap().is_empty());
}

#[test]
fn a_filesystem_that_cannot_be_measured_refuses_the_write() {
    let host = FakePanelChallengeHost::unmeasurable();

    let refused = write_panel_challenge(&host, &floor(), &token(), &key_authorization());

    assert!(matches!(
        refused,
        Err(SslOpError::FreeSpace(FloorRefusal::Unmeasurable))
    ));
    assert!(host.files.lock().unwrap().is_empty());
}
