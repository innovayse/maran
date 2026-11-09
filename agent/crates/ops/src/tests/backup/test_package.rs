//! Shared fixtures of the moved-backup tests: a key, a signed manifest, and the
//! two ends of a stream played from memory.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use aws_lc_rs::rand::SystemRandom;
use aws_lc_rs::signature::{ECDSA_P384_SHA384_ASN1_SIGNING, EcdsaKeyPair};
use sha2::{Digest as _, Sha256};

use crate::backup::model::export_sink::ExportSink;
use crate::backup::model::export_stopped::ExportStopped;
use crate::backup::model::import_interrupted::ImportInterrupted;
use crate::backup::model::import_source::ImportSource;
use crate::backup::package::lowercase_hex::lowercase_hex;
use crate::backup::package::package_signing_key::PackageSigningKey;
use crate::backup::package::signed_manifest::SignedManifest;
use crate::backup::package::transfer_manifest::{TRANSFER_MANIFEST_VERSION, TransferManifest};

/// A freshly generated ECDSA P-384 signing key.
pub(crate) fn generated_key() -> PackageSigningKey {
    let document =
        EcdsaKeyPair::generate_pkcs8(&ECDSA_P384_SHA384_ASN1_SIGNING, &SystemRandom::new())
            .unwrap();

    PackageSigningKey::from_pkcs8(document.as_ref()).unwrap()
}

/// The lowercase hexadecimal SHA-256 of `bytes`.
pub(crate) fn sha256_of(bytes: &[u8]) -> String {
    let mut hasher = Sha256::new();
    hasher.update(bytes);
    lowercase_hex(&hasher.finalize())
}

/// The manifest a sender would sign for `artifact`, naming `account`'s backup
/// `backup_id` with `databases`, signed by `key`.
pub(crate) fn manifest_for(
    key: &PackageSigningKey,
    account: &str,
    backup_id: &str,
    artifact: &[u8],
    databases: &[String],
) -> TransferManifest {
    TransferManifest {
        version: TRANSFER_MANIFEST_VERSION,
        account: account.to_owned(),
        backup_id: backup_id.to_owned(),
        artifact_bytes: artifact.len() as u64,
        artifact_sha256: sha256_of(artifact),
        home_bytes: 10,
        databases: databases.to_vec(),
        created_at_unix: 1_767_225_600,
        agent_version: "0.0.0".to_owned(),
        signer_fingerprint: key.fingerprint(),
    }
}

/// `manifest` serialised and signed with `key`.
pub(crate) fn signed(key: &PackageSigningKey, manifest: &TransferManifest) -> SignedManifest {
    signed_bytes(key, serde_json::to_vec(manifest).unwrap())
}

/// Exactly `bytes`, signed with `key`: for manifests no honest sender would write.
pub(crate) fn signed_bytes(key: &PackageSigningKey, bytes: Vec<u8>) -> SignedManifest {
    SignedManifest {
        signature: key.sign(&bytes).unwrap(),
        manifest: bytes,
        public_key: key.public_key(),
    }
}

/// An import stream played from memory.
pub(crate) struct ChunkSource {
    /// What is left to deliver.
    chunks: Vec<Vec<u8>>,
    /// Whether the stream ends abnormally after the last chunk.
    interrupted: bool,
    /// How many times the importer asked.
    asked: usize,
    /// The chunk handed out last.
    current: Vec<u8>,
}

impl ChunkSource {
    /// A stream that delivers `chunks` and then ends cleanly.
    pub(crate) fn clean(chunks: Vec<Vec<u8>>) -> Self {
        Self {
            chunks,
            interrupted: false,
            asked: 0,
            current: Vec::new(),
        }
    }

    /// A stream that delivers `chunks` and then breaks.
    pub(crate) fn interrupted(chunks: Vec<Vec<u8>>) -> Self {
        Self {
            interrupted: true,
            ..Self::clean(chunks)
        }
    }

    /// How many times the importer asked for a chunk.
    pub(crate) fn asked(&self) -> usize {
        self.asked
    }
}

impl ImportSource for ChunkSource {
    fn next_chunk(&mut self) -> Result<Option<&[u8]>, ImportInterrupted> {
        self.asked += 1;
        if self.chunks.is_empty() {
            return if self.interrupted {
                Err(ImportInterrupted)
            } else {
                Ok(None)
            };
        }
        self.current = self.chunks.remove(0);

        Ok(Some(&self.current))
    }
}

/// An export sink that keeps what it was given, and can be told to stop.
pub(crate) struct CollectingSink {
    /// The header, once delivered.
    pub(crate) header: Option<SignedManifest>,
    /// The size of every chunk, in order.
    pub(crate) chunk_sizes: Vec<usize>,
    /// Every artifact byte delivered.
    pub(crate) bytes: Vec<u8>,
    /// Whether the header is refused.
    pub(crate) refuse_header: bool,
    /// The chunk number (from zero) at which the sink stops, if any.
    pub(crate) stop_at_chunk: Option<usize>,
}

impl CollectingSink {
    /// A sink that takes everything.
    pub(crate) fn new() -> Self {
        Self {
            header: None,
            chunk_sizes: Vec::new(),
            bytes: Vec::new(),
            refuse_header: false,
            stop_at_chunk: None,
        }
    }
}

impl ExportSink for CollectingSink {
    fn header(&mut self, signed: &SignedManifest) -> Result<(), ExportStopped> {
        if self.refuse_header {
            return Err(ExportStopped);
        }
        self.header = Some(signed.clone());

        Ok(())
    }

    fn chunk(&mut self, data: &[u8]) -> Result<(), ExportStopped> {
        if self.stop_at_chunk == Some(self.chunk_sizes.len()) {
            return Err(ExportStopped);
        }
        self.chunk_sizes.push(data.len());
        self.bytes.extend_from_slice(data);

        Ok(())
    }
}
