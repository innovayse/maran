//! Fleet mode on the agent (issue #180): the node's identity, and the TCP listener that lets
//! one panel drive this node.
//!
//! The listener is OFF unless the daemon is handed a [`fleet_listener::FleetListener`]. When it
//! is on, the same router, services and process-wide locks serve it as serve the unix socket;
//! what differs is only who is admitted. A network peer must complete a TLS 1.3 handshake
//! presenting the panel certificate this node pinned at enrolment, and is checked against that
//! pin again on every request. There is no certificate authority and no unauthenticated rpc.
//!
//! Two halves live here. The identity half (`generate_node_identity`, `node_identity`,
//! `panel_pin` and the files they read and write) creates and reads what sits in the fleet
//! directory. The listener half (everything else) turns that into a TLS acceptor, a verifier, a
//! bounded accept loop and an admission layer.

mod accept_fleet_connections;
pub mod agent_incoming;
pub mod bound_fleet_listener;
pub mod build_fleet_listener;
mod certificate_bytes;
mod complete_handshake;
mod ensure_private_directory;
pub mod fleet_admission_layer;
pub mod fleet_admission_refusal;
pub mod fleet_admission_service;
pub mod fleet_crypto_provider;
pub mod fleet_handshake_failure;
pub mod fleet_identity_error;
pub mod fleet_limits;
pub mod fleet_listener;
pub mod fleet_listener_error;
pub mod fleet_server_config;
pub mod generate_node_identity;
pub mod node_identity;
pub mod node_tls_identity;
pub mod panel_pin;
pub mod panel_pin_file;
pub mod pin_slot;
pub mod pin_source;
pub mod pinned_client_verifier;
pub mod pinned_panel_certificate;
mod read_identity_file;
mod require_private_directory;
mod write_new_identity_file;

/// Test-only: a private directory and an identity generated into it, mounted once for every
/// test file that needs a real identity to read back.
#[cfg(test)]
#[path = "../tests/fleet/fixture_identity.rs"]
pub(crate) mod fixture_identity;

/// Test-only: self-signed certificates with a stated validity and key usage, for the tests of
/// the verifier, the admission service and the listener.
#[cfg(test)]
#[path = "../tests/fleet/test_certificates.rs"]
pub(crate) mod test_certificates;

/// Test-only: TLS clients with a stated certificate and protocol version, for the tests of the
/// accept loop and the listener.
#[cfg(test)]
#[path = "../tests/fleet/tls_test_client.rs"]
pub(crate) mod tls_test_client;

/// Test-only: the server side of the listener's TLS over a test node certificate, for the tests
/// of the accept loop and of one handshake.
#[cfg(test)]
#[path = "../tests/fleet/test_acceptor.rs"]
pub(crate) mod test_acceptor;
