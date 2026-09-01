//! The command that checks a rendered configuration before it is swapped in.

/// The command [`super::super::render_validate_swap::write_config`] runs
/// against the freshly written temporary file to decide whether it is safe
/// to put in place — `nginx -t`, `php-fpm -t`, `crontab -T`, as the area
/// requires.
///
/// Holds only the argv, never a path: the program and its arguments are
/// facts of the distribution, supplied by the caller from its
/// `DistroAdapter`, and this crate must not guess at either
/// (rules/rust.md "Distro adapter").
pub struct Validator<'a> {
    /// Absolute path of the validating binary.
    pub program: &'a str,
    /// Arguments passed to `program`, as an argv array — never a shell string.
    pub arguments: &'a [&'a str],
}
