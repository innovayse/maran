//! How much of each source's path the archive leaves out.

use maran_agent_core::validation::fs::browse_path::BrowsePath;

/// The length in bytes, separator included, of the directory all `sources` share.
///
/// Sources `sites/a/public` and `sites/a/notes.txt` share `sites/a`, so their
/// archive names start at `public` and `notes.txt`; a single source `sites/a`
/// shares `sites` with itself, so the archive holds `a/...`. Sources with
/// nothing in common (`sites/a` and `docs`) share the home: length zero, and the
/// archive names are the whole paths.
///
/// Decided on components, never on bytes, so `site` and `site2` do not share
/// `site`. A pure function of validated paths; it allows nothing, it only picks
/// where a name begins.
pub(crate) fn common_parent_length(sources: &[BrowsePath]) -> usize {
    let mut shared: Option<Vec<&str>> = None;
    for source in sources {
        let components = source.components();
        let parent: Vec<&str> = components[..components.len().saturating_sub(1)]
            .iter()
            .map(String::as_str)
            .collect();
        shared = Some(match shared {
            None => parent,
            Some(current) => current
                .into_iter()
                .zip(parent)
                .take_while(|(left, right)| left == right)
                .map(|(left, _)| left)
                .collect(),
        });
    }

    shared
        .filter(|components| !components.is_empty())
        .map_or(0, |components| components.join("/").len() + 1)
}

#[cfg(test)]
#[path = "../../tests/files/archive/common_parent_length_tests.rs"]
mod tests;
