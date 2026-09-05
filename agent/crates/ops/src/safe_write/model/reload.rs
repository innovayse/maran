//! The command that makes a swapped-in configuration take effect.

/// The command [`super::super::render_validate_swap::write_config`] runs
/// after the new configuration has replaced the old one — typically a
/// `systemctl reload` of the service that reads the target file.
///
/// Holds only the argv, never a path, for the same reason as [`super::Validator`]:
/// the program and its arguments are distribution facts supplied by the
/// caller, not something this crate constructs itself.
pub struct Reload<'a> {
    /// Absolute path of the binary that performs the reload.
    pub program: &'a str,
    /// Arguments passed to `program`, as an argv array — never a shell string.
    pub arguments: &'a [&'a str],
}
