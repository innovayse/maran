//! Installing a zone's signing key where the daemon looks for it.

use std::path::Path;

use maran_agent_core::validation::dns::dnssec_key_file_name::DnssecKeyFileName;
use maran_agent_core::validation::dns::dnssec_key_material::DnssecKeyMaterial;
use maran_agent_core::validation::dns::zone_name::ZoneName;
use maran_distro::adapter::DistroAdapter;
use maran_templates::bind::dnssec_private_key::DnssecPrivateKey;
use maran_templates::bind::dnssec_public_key::DnssecPublicKey;

use crate::dns::dns_error::DnsError;
use crate::dns::zone_host::ZoneHost;

/// Writes `key`'s two files, `K<zone>.+013+<tag>.private` and `.key`, into
/// `keys`, owned by root and readable by the daemon's group.
///
/// **Where the bytes come from.** The names are built by [`DnssecKeyFileName`]
/// from the validated zone and the key tag the agent RECOMPUTED, never from a
/// caller-supplied tag, and the bodies are rendered from the validated key by the
/// templates crate. Nothing a request typed reaches either, and the private half
/// is read through the one named reader, `expose_private_key_base64`.
///
/// **How they are written.** Each file goes through [`ZoneHost::write_key_file`],
/// the config-write protocol's temporary file, `fsync` and atomic rename, which
/// replaces a name and never follows one: a planted symlink in the writable
/// directory is overwritten, not written through. The private file goes first, so
/// a public file is never left without the key it names; if the second write fails
/// the first is removed again.
///
/// Idempotent: the same key writes the same two files.
///
/// **Memory.** The rendered private file is a `String` that is dropped when this
/// returns and is not zeroised, like the `SecretString` it was read from. Stated,
/// not removed.
///
/// # Errors
///
/// Returns [`DnsError::ConfigWrite`] when the file name would be too long, when
/// the daemon's group cannot be resolved or a file cannot be written, and
/// [`DnsError::Render`] when a template and its render type have drifted apart.
/// No variant carries a byte of the key.
pub(crate) fn write_signing_keys(
    host: &dyn ZoneHost,
    distro: &dyn DistroAdapter,
    keys: &Path,
    zone: &ZoneName,
    key: &DnssecKeyMaterial,
) -> Result<(), DnsError> {
    let name = DnssecKeyFileName::of(zone, key).map_err(|error| DnsError::ConfigWrite {
        reason: error.to_string(),
    })?;

    let private_text = DnssecPrivateKey {
        algorithm: key.algorithm(),
        private_key: key.expose_private_key_base64(),
    }
    .render_config()
    .map_err(|_| DnsError::Render)?;

    let apex = zone.fully_qualified();
    let public_text = DnssecPublicKey {
        zone: &apex,
        key_tag: key.key_tag(),
        flags: key.flags(),
        protocol: key.protocol(),
        algorithm: key.algorithm(),
        public_key: key.public_key_base64(),
    }
    .render_config()
    .map_err(|_| DnsError::Render)?;

    let group = distro.dns_group();
    let private_path = keys.join(name.private_file());
    host.write_key_file(&private_path, &private_text, group)?;

    if let Err(failed) = host.write_key_file(&keys.join(name.public_file()), &public_text, group) {
        if let Err(rollback) = host.remove_file(&private_path) {
            tracing::error!(
                rollback = %rollback,
                "a signing key's private file could not be removed after its public file failed"
            );
        }
        return Err(failed);
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/dns/write_signing_keys_tests.rs"]
mod tests;
