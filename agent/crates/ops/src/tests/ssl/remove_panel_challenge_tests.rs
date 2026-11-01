//! What `remove_panel_challenge` removes, and how it answers a second removal.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_agent_core::validation::web::acme_key_authorization::AcmeKeyAuthorization;
use maran_agent_core::validation::web::acme_token::AcmeToken;

use super::*;
use crate::ssl::fake_panel_challenge_host::FakePanelChallengeHost;
use crate::ssl::write_panel_challenge::write_panel_challenge;

#[test]
fn a_written_challenge_is_removed_and_a_second_removal_reports_not_found() {
    let host = FakePanelChallengeHost::with_room(u64::MAX);
    let token = AcmeToken::parse("tok").unwrap();
    let authorization = AcmeKeyAuthorization::parse(&token, "tok.abc").unwrap();
    write_panel_challenge(
        &host,
        &FreeSpaceFloor::from_mebibytes(1).unwrap(),
        &token,
        &authorization,
    )
    .unwrap();

    remove_panel_challenge(&host, &token).unwrap();

    assert!(host.files.lock().unwrap().is_empty());
    assert!(matches!(
        remove_panel_challenge(&host, &token),
        Err(SslOpError::ChallengeNotFound)
    ));
}
