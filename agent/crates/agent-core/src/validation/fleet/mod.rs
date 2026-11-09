//! Values that belong to fleet mode (issue #180): where a node's agent listens, what its
//! certificate is called, and the SHA-256 fingerprint two machines compare to decide whom they
//! trust. The values are checked once here and carried as types, like every other kind in this
//! tree.

pub mod certificate_fingerprint;
pub mod certificate_fingerprint_error;
pub mod listen_address;
pub mod listen_address_error;
pub mod node_address;
pub mod node_address_error;
