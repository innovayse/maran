//! The one cryptography provider the fleet listener uses.

use std::sync::Arc;

use rustls::crypto::CryptoProvider;

/// The aws-lc-rs provider, as a value to hand to every TLS configuration explicitly.
///
/// Explicit and not "the process default": rustls panics in `ServerConfig::builder()` when the
/// crate features name two providers and none was installed, and that would happen only in code
/// that runs when the fleet flags are set, where no test that never starts the listener would
/// see it. Naming the provider at each use makes the
/// choice independent of feature unification. The provider is aws-lc-rs because it is the one
/// already compiled into the agent beneath its S3 client; `ring` is never selected.
#[must_use]
pub fn fleet_crypto_provider() -> Arc<CryptoProvider> {
    Arc::new(rustls::crypto::aws_lc_rs::default_provider())
}
