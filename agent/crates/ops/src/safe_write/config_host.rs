//! The seam between the config-write protocol and the machine it runs on.

use crate::safe_write::{CommandOutcome, SafeWriteError};

/// The operating-system operation the config-write protocol needs: running a
/// validator or a reload command.
///
/// A trait rather than a direct call to `std::process::Command`, for the same
/// reason `accounts::SystemHost` is one: validating and reloading a real web
/// server are exactly the operations a test of the rollback protocol must
/// never really perform. The one implementation that actually spawns
/// processes belongs to the caller that owns a `DistroAdapter` — this crate
/// only defines the seam.
///
/// Implementations must spawn with an argv array and never through a shell
/// (rules/security.md item 3): `program` and `arguments` are expected to come
/// from a `DistroAdapter`'s process-execution allow-list, not from anything
/// caller-supplied at the value level.
pub trait ConfigHost: Send + Sync {
    /// Runs `program` with `arguments` and waits for it.
    ///
    /// # Errors
    ///
    /// Returns [`SafeWriteError::SpawnFailed`] when the program cannot be
    /// started at all — that variant and not a refusal of the content, because
    /// an operator installing a missing package and one fixing a rejected
    /// config are doing different work. A non-zero exit is NOT an error here —
    /// it is returned in the outcome, because
    /// [`super::render_validate_swap::write_config`] reads a validation exit
    /// status differently from a reload exit status.
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, SafeWriteError>;
}
