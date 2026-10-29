//! Everything the bounded core needs to run one program.

use std::path::Path;

use crate::utils::call_class::CallClass;
use crate::utils::child_input::ChildInput;
use crate::utils::child_output::ChildOutput;

/// One program to run, and every bound it runs under.
///
/// Every field is stated by the caller; nothing is defaulted inside the core,
/// so a reader of a call site sees the whole contract of that spawn.
#[derive(Debug)]
pub struct SpawnRequest<'a> {
    /// The absolute path of the program. It comes from the `DistroAdapter`'s
    /// allow-list and never from a request.
    pub program: &'a Path,
    /// The argument vector, one entry per `execve` argument. No shell parses it.
    pub arguments: &'a [&'a str],
    /// The call class, which decides the deadline.
    pub class: CallClass,
    /// What the child reads from standard input.
    pub input: ChildInput<'a>,
    /// Whether and how the child's output is captured.
    pub output: ChildOutput,
}
