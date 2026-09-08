//! Tests for the mapping from a classified shadow field onto the wire enum.

use maran_ops::accounts::StoredPassword;

use crate::proto::LoginPasswordState;
use crate::services::accounts::to_login_password_state::to_login_password_state;

/// Each classified state maps onto its own wire value.
///
/// Written as one table rather than four tests because the defect this guards
/// against is a SWAP: `Absent` and `Locked` are the pair `passwd -S` collapses,
/// and exchanging them would make the panel refuse every hosting account again
/// while every value stayed "valid".
#[test]
fn each_stored_password_state_maps_onto_its_own_wire_value() {
    assert_eq!(
        to_login_password_state(StoredPassword::Empty),
        LoginPasswordState::Empty
    );
    assert_eq!(
        to_login_password_state(StoredPassword::Absent),
        LoginPasswordState::Absent
    );
    assert_eq!(
        to_login_password_state(StoredPassword::Locked),
        LoginPasswordState::Locked
    );
    assert_eq!(
        to_login_password_state(StoredPassword::Usable),
        LoginPasswordState::Usable
    );
}

/// The unspecified wire value is never produced by the mapping.
///
/// On the wire it means "this agent predates the field", and an agent that can
/// classify the field always has one of the four real answers. If this ever
/// fires, a panel would fall back to `login_locked` — the very boolean the
/// field exists to stop deciding reactivations.
#[test]
fn no_classified_state_maps_onto_unspecified() {
    for stored in [
        StoredPassword::Empty,
        StoredPassword::Absent,
        StoredPassword::Locked,
        StoredPassword::Usable,
    ] {
        assert_ne!(
            to_login_password_state(stored),
            LoginPasswordState::Unspecified
        );
    }
}
