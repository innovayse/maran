//! The package a backup is moved between servers in: a manifest the exporting
//! host signs and the importing host verifies, and the digest and key
//! fingerprint helpers both sides share.
//!
//! A package is the existing backup artifact, byte for byte, plus a signed
//! manifest naming the artifact's size and SHA-256. The signature is ECDSA
//! P-384 over SHA-384 (rules/security.md item 9), made with a per-server key the
//! panel holds and sends with the export call. The importing host trusts no key
//! it is not told to: the operator confirms the signer's fingerprint against the
//! sending server, and the import refuses any other key however valid the
//! signature is.

pub mod key_fingerprint;
pub mod lowercase_hex;
pub mod package_signing_key;
pub mod signed_manifest;
pub mod transfer_manifest;
pub mod verify_package;
