//! Mints the key pair of one source.

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::secrets::source_private_key::SourcePrivateKey;
use maran_agent_core::validation::system::ssh_public_key::SshPublicKey;
use maran_agent_core::validation::web::host_key::HostKey;
use maran_distro::DistroAdapter;

use crate::transfer::model::generated_source_key::GeneratedSourceKey;
use crate::transfer::ssh_key_fingerprint::ssh_key_fingerprint;
use crate::transfer::transfer_host::TransferHost;
use crate::transfer::transfer_op_error::TransferOpError;
use crate::transfer::transfer_scratch::{KEY_FILE_NAME, TransferScratch};

/// The most bytes of either half read back from the scratch file. An ed25519
/// private key document is about 400 bytes; this is far above it and the same
/// ceiling the validated type applies.
const KEY_FILE_LIMIT_BYTES: u64 = 16 * 1024;

/// The most standard output `ssh-keygen` may print with `-q`.
const KEYGEN_OUTPUT_LIMIT_BYTES: u64 = 16 * 1024;

/// The only algorithm a source key may be.
const ALGORITHM: &str = "ssh-ed25519";

/// Generates one ed25519 key pair with `ssh-keygen`, in a scratch directory only
/// this process can enter, reads both halves back and removes the directory.
///
/// No passphrase and no comment are set. The agent keeps nothing: the panel
/// stores the private half encrypted and sends it back on every later call. No
/// network is touched. The scratch directory is removed on every path, by the
/// guard that owns it, including when a read or a parse below fails.
///
/// # Errors
///
/// Returns [`TransferOpError::KeyGenerationFailed`] when `ssh-keygen` exits
/// non-zero or either half it wrote is not a key this agent accepts (the
/// public half must be an ed25519 key), [`TransferOpError::ScratchUnavailable`]
/// when the scratch area cannot be made, and the bound and spawn errors of the
/// program run.
pub fn generate_source_key(
    host: &dyn TransferHost,
    distro: &dyn DistroAdapter,
) -> Result<GeneratedSourceKey, TransferOpError> {
    let scratch = TransferScratch::create(host)?;
    let private_path = scratch.file(KEY_FILE_NAME);
    let public_path = scratch.file(&format!("{KEY_FILE_NAME}.pub"));
    let private_argument = private_path
        .to_str()
        .ok_or(TransferOpError::ScratchUnavailable)?;

    let outcome = host.run(
        &distro.ssh_keygen_binary(),
        &[
            "-q",
            "-t",
            "ed25519",
            "-N",
            "",
            "-C",
            "",
            "-f",
            private_argument,
        ],
        CallClass::Mutation,
        KEYGEN_OUTPUT_LIMIT_BYTES,
    )?;
    if outcome.status != 0 {
        return Err(TransferOpError::KeyGenerationFailed);
    }

    let private_document = host
        .read_small_file(&private_path, KEY_FILE_LIMIT_BYTES)
        .map_err(|_| TransferOpError::KeyGenerationFailed)?;
    let public_document = host
        .read_small_file(&public_path, KEY_FILE_LIMIT_BYTES)
        .map_err(|_| TransferOpError::KeyGenerationFailed)?;

    let private_key = SourcePrivateKey::parse(private_document.as_bytes())
        .map_err(|_| TransferOpError::KeyGenerationFailed)?;
    let public_key = SshPublicKey::parse(public_document.trim())
        .map_err(|_| TransferOpError::KeyGenerationFailed)?;
    if public_key.algorithm() != ALGORITHM {
        return Err(TransferOpError::KeyGenerationFailed);
    }
    let fingerprint = ssh_key_fingerprint(
        &HostKey::parse(&public_key.as_line()).map_err(|_| TransferOpError::KeyGenerationFailed)?,
    );

    Ok(GeneratedSourceKey {
        public_key,
        private_key,
        fingerprint,
    })
}

#[cfg(test)]
#[path = "../tests/transfer/generate_source_key_tests.rs"]
mod tests;
