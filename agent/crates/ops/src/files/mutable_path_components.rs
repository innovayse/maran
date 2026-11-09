//! Every component of a mutable path, in order.

use maran_agent_core::validation::fs::mutable_path::MutablePath;

/// The components of `path`, parents first and the final name last.
///
/// What the containment questions of a move and a copy compare ("is the
/// destination inside the source") and what the trash writes down as an entry's
/// original place. The components are already proved to be plain entry names,
/// so a lexical comparison is sound: a descent that follows no symlink at any
/// level makes the lexical position the real one.
#[must_use]
pub(crate) fn mutable_path_components(path: &MutablePath) -> Vec<&str> {
    let mut components: Vec<&str> = path
        .parent_components()
        .iter()
        .map(String::as_str)
        .collect();
    components.push(path.file_name());
    components
}

#[cfg(test)]
#[path = "../tests/files/mutable_path_components_tests.rs"]
mod tests;
