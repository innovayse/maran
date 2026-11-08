//! How a family switches one PHP extension on and off.

use crate::adapter_binary::AdapterBinary;

/// The two programs-with-arguments a family uses to switch one PHP extension
/// on and off for one version's php-fpm.
///
/// Both halves are complete: a program and the whole `argv` after it, built by
/// the adapter from a validated `PhpVersion` and a catalogue
/// `PhpExtensionName`, so no part of either comes from a caller. Enable and
/// disable name a program each because a family may ship them as two tools —
/// the Debian family's `phpenmod` and `phpdismod` are two files, and the tool
/// decides which it does by the name it was started under, so one program
/// plus a flag cannot express it.
///
/// A family with no clean switch answers `None` from
/// [`PhpExtensionFacts::php_extension_switch`](crate::php_extension_facts::PhpExtensionFacts::php_extension_switch)
/// and not a value with empty fields.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PhpExtensionSwitch {
    /// The program that switches the extension ON.
    pub enable_program: AdapterBinary,
    /// The whole argument vector after [`Self::enable_program`].
    pub enable_arguments: Vec<String>,
    /// The program that switches the extension OFF.
    pub disable_program: AdapterBinary,
    /// The whole argument vector after [`Self::disable_program`].
    pub disable_arguments: Vec<String>,
}
