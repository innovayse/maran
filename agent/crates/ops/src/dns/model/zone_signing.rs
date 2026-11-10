//! What a zone is asked to be about DNSSEC, with the key when it is signed.

use maran_agent_core::validation::dns::dnssec_key_material::DnssecKeyMaterial;

use crate::dns::model::signing_mode::SigningMode;

/// Whether and how one zone is signed, and the key that signs it.
///
/// A key is carried by exactly the two modes that need one, so "signed with no
/// key" and "unsigned with a key" are not values: the first would have the
/// daemon invent a key the registrar's DS record does not name, the second would
/// leave a private key on the host for a zone that no longer uses it.
///
/// `Debug` is derived and is safe: [`DnssecKeyMaterial`] prints its private half
/// as `«redacted»`. There is deliberately no `PartialEq`: the key has none.
#[derive(Debug, Clone, Default)]
pub enum ZoneSigning {
    /// The zone is not signed and has no key on the host.
    #[default]
    Unsigned,
    /// The zone is signed with this key under the panel's policy.
    Signed(DnssecKeyMaterial),
    /// The zone is being withdrawn from signing, still served with this key.
    Withdrawing(DnssecKeyMaterial),
}

impl ZoneSigning {
    /// The phase, without the key.
    #[must_use]
    pub fn mode(&self) -> SigningMode {
        match self {
            Self::Unsigned => SigningMode::Unsigned,
            Self::Signed(_) => SigningMode::Signed,
            Self::Withdrawing(_) => SigningMode::Withdrawing,
        }
    }

    /// The key, when the zone has one.
    #[must_use]
    pub fn key(&self) -> Option<&DnssecKeyMaterial> {
        match self {
            Self::Unsigned => None,
            Self::Signed(key) | Self::Withdrawing(key) => Some(key),
        }
    }
}

#[cfg(test)]
#[path = "../../tests/dns/model/zone_signing_tests.rs"]
mod tests;
