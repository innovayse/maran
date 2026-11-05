//! How a family finds out whether the host needs a reboot.

use std::path::PathBuf;

use crate::adapter_binary::AdapterBinary;

/// The way a family answers "is a reboot required?" (issue #163).
///
/// Two shapes, because the two families differ in kind and not in spelling: one
/// leaves a marker file behind, the other has a program that computes the
/// answer. The caller runs whichever it is handed and reads a program's run
/// through [`crate::PackageFacts::read_reboot_probe_run`]; it writes neither a
/// path nor a program name of its own.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum RebootProbe {
    /// A marker file the package manager's hooks create: present means a reboot
    /// is required. Absence answers "not required" only when the stat itself
    /// succeeded and reported the file missing; any other stat error is a
    /// failure to find out.
    Marker(PathBuf),
    /// A program run with constant arguments and no operand: its exit status is
    /// read by [`crate::PackageFacts::read_reboot_probe_run`].
    Program {
        /// The program to start, constructible only by this crate.
        binary: AdapterBinary,
        /// The fixed arguments; no caller value reaches them.
        arguments: Vec<String>,
    },
}
