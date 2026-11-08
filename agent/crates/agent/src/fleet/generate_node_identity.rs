//! Generating this node's fleet identity.

use std::path::Path;
use std::time::SystemTime;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::fleet::certificate_fingerprint::CertificateFingerprint;
use maran_agent_core::validation::fleet::node_address::NodeAddress;
use rcgen::string::Ia5String;
use rcgen::{
    CertificateParams, DistinguishedName, DnType, ExtendedKeyUsagePurpose, IsCa, KeyPair,
    KeyUsagePurpose, PKCS_ECDSA_P256_SHA256, SanType,
};
use time::{Duration, OffsetDateTime};

use super::ensure_private_directory::ensure_private_directory;
use super::fleet_identity_error::FleetIdentityError;
use super::write_new_identity_file::write_new_identity_file;
use crate::config::node_identity_request::NodeIdentityRequest;

/// How long the certificate is valid for: two years. Renewal is a new enrolment, so a shorter
/// life is a recurring chore and a longer one a key that outlives its owner's attention. Kept
/// well inside the 800 days the panel accepts at enrolment.
const VALIDITY_DAYS: i64 = 730;

/// How far before the generation time the certificate becomes valid. A panel whose clock is a
/// little behind this node's would otherwise refuse a certificate "from the future".
const BACKDATE_HOURS: i64 = 1;

/// The label every node certificate subject starts with.
const COMMON_NAME_PREFIX: &str = "maran-node-";

/// How many hex characters of the public key's hash the subject carries.
const COMMON_NAME_SUFFIX_LENGTH: usize = 16;

/// Generates this node's keypair and self-signed certificate and writes them into the fleet
/// directory, returning the certificate's fingerprint.
///
/// What is made, with every field stated and none left to a library default (the defaults of the
/// generator are a validity of 1975 to 4096 and one constant subject, wrong for a certificate a
/// peer is asked to trust):
///
/// - an ECDSA P-256 key, which every supported distribution's crypto policy permits;
/// - a validity of `VALIDITY_DAYS` days starting `BACKDATE_HOURS` hour before `now`;
/// - a subject `CN=maran-node-<16 hex of the public key's hash>`, so no two nodes share one;
/// - one subject alternative name, the declared `address`, as an IP or a DNS name;
/// - extended key usage `serverAuth` only, key usage `digitalSignature` only, not a CA.
///
/// The directory is created `0700` when absent and required to be private when present. The key
/// is written first and only ever with mode `0600`, created fresh (`O_EXCL`), so it is never
/// readable by anyone else even for an instant; the key never leaves this host.
///
/// An identity already in the directory is refused unless `request.replace`, because replacing it
/// cuts the node off from the panel approved against the old one. With `replace`, both old files
/// are removed first and the new ones created fresh, so a crash in between leaves no identity,
/// not a key that does not belong to its certificate; a crash after the key is written leaves a
/// key and no certificate, which the next run treats as an existing identity to replace.
///
/// `owner_uid` is the uid the directory must belong to (the agent's own), and `now` is the
/// clock: both are parameters so a test can use a directory it owns and a fixed time.
///
/// # Errors
///
/// - Every directory error of the private-directory check (not a plain directory, owned by
///   another, group or world accessible) and [`FleetIdentityError::Io`] when it cannot be
///   created.
/// - [`FleetIdentityError::IdentityAlreadyExists`] when a key or certificate is already there
///   and `request.replace` is false.
/// - [`FleetIdentityError::ClockOutOfRange`] when `now` cannot be expressed as a validity.
/// - [`FleetIdentityError::GenerationFailed`] when the key or certificate cannot be made.
/// - [`FleetIdentityError::Io`] when a file cannot be removed, written or synced. If the
///   certificate cannot be written the key just written is removed again.
pub fn generate_node_identity(
    request: &NodeIdentityRequest,
    owner_uid: u32,
    now: SystemTime,
) -> Result<CertificateFingerprint, FleetIdentityError> {
    let directory = request.directory.as_path();
    ensure_private_directory(directory, owner_uid)?;

    let key_path = directory.join(AgentPaths::FLEET_NODE_KEY_FILE);
    let certificate_path = directory.join(AgentPaths::FLEET_NODE_CERTIFICATE_FILE);

    if request.replace {
        remove_existing(&key_path)?;
        remove_existing(&certificate_path)?;
        sync_directory(directory)?;
    } else {
        refuse_existing(&key_path)?;
        refuse_existing(&certificate_path)?;
    }

    let generation_failed = |error: rcgen::Error| FleetIdentityError::GenerationFailed {
        reason: error.to_string(),
    };

    let key_pair = KeyPair::generate_for(&PKCS_ECDSA_P256_SHA256).map_err(generation_failed)?;
    let params = certificate_params(&request.address, &key_pair, now)?;
    let certificate = params.self_signed(&key_pair).map_err(generation_failed)?;
    let fingerprint = CertificateFingerprint::of_der(certificate.der().as_ref());

    write_new_identity_file(
        &key_path,
        key_pair.serialize_pem().as_bytes(),
        AgentPaths::FLEET_FILE_MODE,
    )?;
    if let Err(error) = write_new_identity_file(
        &certificate_path,
        certificate.pem().as_bytes(),
        AgentPaths::FLEET_FILE_MODE,
    ) {
        // A key with no certificate is a half identity; take it away so the next run starts
        // clean. Best effort: the error being returned is the one that matters.
        let _ = std::fs::remove_file(&key_path);
        return Err(error);
    }
    sync_directory(directory)?;

    Ok(fingerprint)
}

/// Builds the certificate's parameters, every field stated.
///
/// # Errors
///
/// Returns [`FleetIdentityError::ClockOutOfRange`] when `now` plus the validity overflows, and
/// [`FleetIdentityError::GenerationFailed`] when the address cannot be a DNS name entry.
fn certificate_params(
    address: &NodeAddress,
    key_pair: &KeyPair,
    now: SystemTime,
) -> Result<CertificateParams, FleetIdentityError> {
    let issued = OffsetDateTime::from(now);
    let not_before = issued
        .checked_sub(Duration::hours(BACKDATE_HOURS))
        .ok_or(FleetIdentityError::ClockOutOfRange)?;
    let not_after = issued
        .checked_add(Duration::days(VALIDITY_DAYS))
        .ok_or(FleetIdentityError::ClockOutOfRange)?;

    let mut subject = DistinguishedName::new();
    subject.push(DnType::CommonName, common_name(key_pair));

    let alternative_name = match address {
        NodeAddress::Ip(ip) => SanType::IpAddress(*ip),
        NodeAddress::Host(domain) => SanType::DnsName(
            Ia5String::try_from(domain.as_str().to_owned()).map_err(|error| {
                FleetIdentityError::GenerationFailed {
                    reason: error.to_string(),
                }
            })?,
        ),
    };

    let mut params = CertificateParams::default();
    params.not_before = not_before;
    params.not_after = not_after;
    params.distinguished_name = subject;
    params.subject_alt_names = vec![alternative_name];
    params.is_ca = IsCa::NoCa;
    params.key_usages = vec![KeyUsagePurpose::DigitalSignature];
    params.extended_key_usages = vec![ExtendedKeyUsagePurpose::ServerAuth];
    Ok(params)
}

/// The subject's common name: the role and a prefix of the public key's hash.
///
/// Derived from the PUBLIC key, which is what makes it unique per node without a source of
/// randomness of its own, and keeps the private key out of anything written into a certificate.
fn common_name(key_pair: &KeyPair) -> String {
    let hash = CertificateFingerprint::of_der(key_pair.public_key_raw());
    let suffix: String = hash
        .as_str()
        .chars()
        .take(COMMON_NAME_SUFFIX_LENGTH)
        .collect();
    format!("{COMMON_NAME_PREFIX}{suffix}")
}

/// Fails when anything exists at `path`, a link included.
///
/// # Errors
///
/// Returns [`FleetIdentityError::IdentityAlreadyExists`] when `path` exists and
/// [`FleetIdentityError::Io`] when its existence cannot be determined.
fn refuse_existing(path: &Path) -> Result<(), FleetIdentityError> {
    match std::fs::symlink_metadata(path) {
        Ok(_) => Err(FleetIdentityError::IdentityAlreadyExists {
            path: path.to_path_buf(),
        }),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(()),
        Err(source) => Err(FleetIdentityError::Io {
            path: path.to_path_buf(),
            source,
        }),
    }
}

/// Removes the file at `path`, and is satisfied when there is none.
///
/// # Errors
///
/// Returns [`FleetIdentityError::Io`] when it exists and cannot be removed.
fn remove_existing(path: &Path) -> Result<(), FleetIdentityError> {
    match std::fs::remove_file(path) {
        Ok(()) => Ok(()),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(()),
        Err(source) => Err(FleetIdentityError::Io {
            path: path.to_path_buf(),
            source,
        }),
    }
}

/// Syncs `directory` itself, so a created or removed entry survives a crash.
///
/// # Errors
///
/// Returns [`FleetIdentityError::Io`] when it cannot be opened or synced.
fn sync_directory(directory: &Path) -> Result<(), FleetIdentityError> {
    std::fs::File::open(directory)
        .and_then(|handle| handle.sync_all())
        .map_err(|source| FleetIdentityError::Io {
            path: directory.to_path_buf(),
            source,
        })
}

#[cfg(test)]
#[path = "../tests/fleet/generate_node_identity_tests.rs"]
mod tests;
