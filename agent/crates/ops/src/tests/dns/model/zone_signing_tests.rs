//! What a zone is asked to be about DNSSEC.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::ZoneSigning;
use crate::dns::fake_zone_host::{PLANTED_PRIVATE_KEY, sample_key};
use crate::dns::model::signing_mode::SigningMode;

#[test]
fn an_unsigned_zone_has_no_key_and_is_the_default() {
    let signing = ZoneSigning::default();

    assert_eq!(signing.mode(), SigningMode::Unsigned);
    assert!(signing.key().is_none());
}

#[test]
fn a_signed_and_a_withdrawing_zone_each_carry_their_key() {
    let signed = ZoneSigning::Signed(sample_key());
    let withdrawing = ZoneSigning::Withdrawing(sample_key());

    assert_eq!(signed.mode(), SigningMode::Signed);
    assert_eq!(withdrawing.mode(), SigningMode::Withdrawing);
    assert_eq!(
        signed.key().unwrap().key_tag(),
        withdrawing.key().unwrap().key_tag()
    );
}

#[test]
fn debug_never_prints_the_private_half() {
    // Positive control: the planted value is the key's, so it is findable in the
    // one place it is allowed to be.
    let signing = ZoneSigning::Signed(sample_key());
    assert_eq!(
        signing.key().unwrap().expose_private_key_base64(),
        PLANTED_PRIVATE_KEY
    );

    assert!(!format!("{signing:?}").contains(PLANTED_PRIVATE_KEY));
}
