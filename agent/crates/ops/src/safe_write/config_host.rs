//! The seam between the config-write protocol and the machine it runs on.

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::AdapterBinary;

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
/// (rules/security.md item 3): `program` is an [`AdapterBinary`], a type only the
/// distribution adapter can construct, and `arguments` are expected to be facts of
/// the distribution, not anything caller-supplied at the value level.
///
/// Implementations must also bound the run: the child gets a deadline from
/// `class`, a process group of its own and a ceiling on what it may print, all of
/// which `maran_agent_core::utils::spawn_argv::spawn_argv` provides.
///
/// A string literal cannot reach the seam:
///
/// ```compile_fail,E0308
/// use maran_agent_core::utils::call_class::CallClass;
/// use maran_ops::safe_write::ConfigHost;
///
/// fn attempt(host: &dyn ConfigHost) {
///     let _ = host.run("nginx", &[], CallClass::Query);
/// }
/// ```
///
/// The inverse control, so the failure above is known to be about the type and
/// not about any other compile error: an adapter's binary reaches the same call.
///
/// ```
/// use maran_agent_core::utils::call_class::CallClass;
/// use maran_distro::{adapter_for, DistroAdapter, DistroFamily};
/// use maran_ops::safe_write::ConfigHost;
///
/// fn attempt(host: &dyn ConfigHost) {
///     let adapter = adapter_for(DistroFamily::Debian);
///     let _ = host.run(&adapter.nginx_binary(), &[], CallClass::Query);
/// }
/// ```
pub trait ConfigHost: Send + Sync {
    /// Runs `program` with `arguments` and waits for it, no longer than `class`
    /// allows.
    ///
    /// # Errors
    ///
    /// Returns [`SafeWriteError::SpawnFailed`] when the program cannot be
    /// started at all — that variant and not a refusal of the content, because
    /// an operator installing a missing package and one fixing a rejected
    /// config are doing different work. Returns [`SafeWriteError::Bound`] when
    /// the program outlived its deadline or its output ceiling and was killed
    /// with its whole process group; that is not a verdict on the
    /// configuration, and it must not be mapped to one. A non-zero exit is NOT an error here —
    /// it is returned in the outcome, because
    /// [`super::render_validate_swap::write_config`] reads a validation exit
    /// status differently from a reload exit status.
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        class: CallClass,
    ) -> Result<CommandOutcome, SafeWriteError>;
}
