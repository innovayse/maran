//! Tests for [`launch_child`], the workspace's one place that starts a process.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::{Path, PathBuf};
use std::process::Stdio;

use super::launch_child;
use crate::utils::process_probe::process_group_of;

/// The child leads a process group of its own: its group id is its pid and not
/// this test process's group.
#[test]
fn the_child_leads_a_process_group_of_its_own() {
    let mut child = launch_child(
        Path::new("/usr/bin/sleep"),
        &["30"],
        Stdio::null(),
        Stdio::null(),
        Stdio::null(),
    )
    .unwrap();
    let pid = child.id();

    let group = process_group_of(pid);
    let ours = process_group_of(std::process::id());
    let _ = child.kill();
    let _ = child.wait();

    assert_eq!(group, Some(pid), "the child is not its own group leader");
    assert_ne!(group, ours);
}

/// Every Rust file under `directory`, test trees and build output skipped.
fn production_sources(directory: &Path, found: &mut Vec<PathBuf>) {
    let Ok(entries) = std::fs::read_dir(directory) else {
        return;
    };
    for entry in entries.flatten() {
        let path = entry.path();
        let name = entry.file_name();
        if path.is_dir() {
            if name != "tests" && name != "target" {
                production_sources(&path, found);
            }
        } else if path.extension().is_some_and(|extension| extension == "rs") {
            found.push(path);
        }
    }
}

/// The census as a test: a process is started in exactly one place, inside the
/// bounded core, and nowhere else in the workspace's production code.
///
/// It walks the crates directory, skips every `tests` folder (a test may start
/// the harness's own helpers) and `target`, and drops comment lines. The positive
/// control is that the scan finds the core's own start.
#[test]
fn every_process_spawn_in_the_workspace_goes_through_the_bounded_core() {
    let crates = Path::new(env!("CARGO_MANIFEST_DIR")).parent().unwrap();
    let core_folder = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("src")
        .join("utils");

    let mut sources = Vec::new();
    production_sources(crates, &mut sources);

    let mut inside_core = 0;
    let mut outside_core = Vec::new();
    for path in sources {
        let text = std::fs::read_to_string(&path).unwrap_or_default();
        let spawns = text
            .lines()
            .filter(|line| !line.trim_start().starts_with("//"))
            .filter(|line| line.contains("Command::new"))
            .count();
        if spawns == 0 {
            continue;
        }
        if path.starts_with(&core_folder) {
            inside_core += spawns;
        } else {
            outside_core.push(path.display().to_string());
        }
    }

    assert!(
        inside_core >= 1,
        "the scan found no spawn in the core; it would pass vacuously"
    );
    assert!(
        outside_core.is_empty(),
        "a process is started outside the bounded core: {outside_core:?}"
    );
}
