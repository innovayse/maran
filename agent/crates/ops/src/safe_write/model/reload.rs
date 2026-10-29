//! The command that makes a swapped-in configuration take effect.

use maran_distro::AdapterBinary;

/// The command [`super::super::render_validate_swap::write_config`] runs
/// after the new configuration has replaced the old one — typically a
/// `systemctl reload` of the service that reads the target file.
///
/// Holds the program as an [`AdapterBinary`] and the argv, for the same reason as
/// [`super::Validator`]: the program and its arguments are distribution facts
/// supplied by the caller, not something this crate constructs itself. It has no
/// call class of its own because the protocol always runs it as
/// [`maran_agent_core::utils::call_class::CallClass::Reload`].
pub struct Reload<'a> {
    /// The binary that performs the reload, as the distribution adapter names it.
    pub program: &'a AdapterBinary,
    /// Arguments passed to `program`, as an argv array — never a shell string.
    pub arguments: &'a [&'a str],
}
