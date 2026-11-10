//! What a `PutZone` request's signing instruction carries, revalidated.

use maran_agent_core::secret_string::SecretString;
use maran_agent_core::validation::dns::dnssec_key_material::DnssecKeyMaterial;
use maran_ops::dns::ZoneSigning;

use crate::proto::{AgentError, DnsSigning, DnsSigningKey, DnsSigningMode};
use crate::services::wire::invalid_input::invalid_input;

/// Turns the wire's signing instruction into the closed [`ZoneSigning`] the
/// operations take.
///
/// - Absent is unsigned: it is what a caller written before signing existed sends.
/// - `UNSIGNED` with a key is REFUSED. A key the zone does not use would be written
///   to the host or silently dropped, and either is a decision the caller did not
///   make; refusing is the only reading that cannot leave a private key where the
///   panel believes none is.
/// - `SIGNED` and `WITHDRAWING` without a key are REFUSED: a daemon told to sign
///   with no key would generate one, and the registrar's DS record names another.
/// - A mode number the enum does not know is refused, not read as unsigned.
/// - The key is parsed by [`DnssecKeyMaterial::parse`], which refuses the wrong
///   lengths, the wrong flags, non-base64, control characters and a key tag that is
///   not the one recomputed from the public key.
///
/// **No error carries the key.** The messages name the field and the rule; the
/// parse error's text is fixed per variant and holds no byte of either half. The
/// private half is wrapped in a [`SecretString`] the moment it leaves the request.
///
/// # Errors
///
/// Returns the wire's invalid-input error naming `signing` for any refusal above.
pub fn validated_signing(signing: Option<&DnsSigning>) -> Result<ZoneSigning, AgentError> {
    let Some(signing) = signing else {
        return Ok(ZoneSigning::Unsigned);
    };

    let mode = DnsSigningMode::try_from(signing.mode).map_err(|_| {
        invalid_input(format!(
            "signing: {} is not a signing mode this agent knows",
            signing.mode
        ))
    })?;

    match (mode, signing.key.as_ref()) {
        (DnsSigningMode::Unsigned, None) => Ok(ZoneSigning::Unsigned),
        (DnsSigningMode::Unsigned, Some(_)) => Err(invalid_input(
            "signing: a key is refused for an unsigned zone".to_owned(),
        )),
        (DnsSigningMode::Signed | DnsSigningMode::Withdrawing, None) => Err(invalid_input(
            "signing: a signed or withdrawing zone needs a key".to_owned(),
        )),
        (DnsSigningMode::Signed, Some(key)) => validated_key(key).map(ZoneSigning::Signed),
        (DnsSigningMode::Withdrawing, Some(key)) => {
            validated_key(key).map(ZoneSigning::Withdrawing)
        }
    }
}

/// Parses the key of a request.
///
/// # Errors
///
/// Returns the wire's invalid-input error carrying the parse error's fixed text.
fn validated_key(key: &DnsSigningKey) -> Result<DnssecKeyMaterial, AgentError> {
    DnssecKeyMaterial::parse(
        &key.public_key,
        &SecretString::new(key.private_key.clone()),
        key.flags,
        key.key_tag,
    )
    .map_err(|error| invalid_input(format!("signing key: {error}")))
}

#[cfg(test)]
#[path = "../../tests/services/dns/validated_signing_tests.rs"]
mod tests;
