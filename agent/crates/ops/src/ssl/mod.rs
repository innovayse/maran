//! TLS certificates: the agent's own store, and the vhost that points at it.
//!
//! **The agent does not know what ACME is.** Ordering a certificate, answering
//! an HTTP-01 challenge and keeping an account key are the panel's work (spec
//! §9: the agent *"only places certificate files and does a reload"*), and this
//! crate has no HTTP client at all. That absence is the enforcement, not a
//! comment: an agent that could fetch a certificate would be an agent that
//! could be told where to fetch one from.
//!
//! What an operation here does is narrow and dangerous, in that order:
//!
//! - It checks the private key against the certificate BEFORE anything is
//!   written. A mismatched pair passes `nginx -t` and fails at the first TLS
//!   handshake, so the site goes down at the exact moment it was supposed to
//!   become secure — and by then the swap has committed and the rollback is
//!   disarmed.
//! - It writes the key first, mode `0600`, owned by root, inside the agent's
//!   own [`AgentPaths::CERTIFICATE_DIRECTORY`](maran_agent_core::agent_paths::AgentPaths)
//!   and never inside a customer's home. The material never goes near
//!   `fork_as_account`: it is the agent's, not the customer's, and an account
//!   that could read it could impersonate the site to every visitor.
//! - It rewires the vhost as a SECOND write, so a certificate nginx rejects
//!   rolls back to the plain-HTTP vhost and leaves a working site.
//! - It cannot let the key reach a log, a message or an error, and that is a
//!   property of the types rather than of anyone's care. A tool handed the key
//!   is run through [`SslHost::run_with_private_key`], whose
//!   [`KeyToolOutcome`](model::key_tool_outcome::KeyToolOutcome) has no stderr
//!   at all and a stdout that can only be compared; a tool handed the
//!   certificate is run through [`SslHost::run_with_certificate`], whose full
//!   output is safe because a certificate is public. [`SslOpError`] has no
//!   variant that can carry key material, and the pair's `Debug` prints
//!   `<redacted>`.
//!
//! The area also owns [`purge_certificate`], which is not an rpc: it is what a
//! site deletion calls to take the material with it. It lives here rather than
//! in `delete_site` because this area already depends on `sites`, and the
//! reverse edge would close a cycle between two areas.
//!
//! Every operation is idempotent as `ssl.proto` requires: installing
//! byte-identical material twice writes nothing, installing different material
//! replaces it, removing when nothing is installed is
//! [`SslOpError::NotFound`], and generating a placeholder over a REAL
//! certificate is refused with [`SslOpError::AlreadyExists`] rather than
//! replacing a trusted certificate with one every browser rejects.

mod certificate_expiry;
mod delete_site_with_certificate;
#[cfg(test)]
#[path = "../tests/ssl/fake_ssl_host.rs"]
pub(crate) mod fake_ssl_host;
mod generate_self_signed;
mod install_certificate;
mod key_matches_certificate;
pub mod model;
mod process_ssl_host;
mod purge_certificate;
mod remove_certificate;
mod remove_material;
mod self_signed_marker;
mod ssl_host;
mod ssl_op_error;
mod write_material;

pub use delete_site_with_certificate::delete_site_with_certificate;
pub use generate_self_signed::generate_self_signed;
pub use install_certificate::install_certificate;
pub use model::certificate_material::CertificateMaterial;
pub use model::self_signed_request::SelfSignedRequest;
pub use process_ssl_host::ProcessSslHost;
pub use purge_certificate::purge_certificate;
pub use remove_certificate::remove_certificate;
pub use ssl_host::SslHost;
pub use ssl_op_error::SslOpError;
