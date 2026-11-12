//! Tests for the `adapter_for_release` module.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::adapter_for_release;
use crate::NodejsPackaging;
use crate::adapter_for::adapter_for;
use crate::detection::distro_info::DistroInfo;
use crate::family::DistroFamily;

fn host(id: &str, family: DistroFamily, version_id: &str) -> DistroInfo {
    DistroInfo {
        id: id.to_owned(),
        family,
        version_id: version_id.to_owned(),
    }
}

#[test]
fn the_release_picks_how_the_rhel_family_packages_node_js() {
    for id in ["almalinux", "rocky", "ol"] {
        assert_eq!(
            adapter_for_release(&host(id, DistroFamily::Rhel, "9.4")).nodejs_packaging(),
            NodejsPackaging::ModuleStreams,
            "{id} 9"
        );
        assert_eq!(
            adapter_for_release(&host(id, DistroFamily::Rhel, "10.2")).nodejs_packaging(),
            NodejsPackaging::PerMajorPackages,
            "{id} 10"
        );
    }
}

#[test]
fn a_release_changes_nothing_for_the_families_without_a_generation_to_tell() {
    // Inverse control: a release string on the other families selects the adapter of the family,
    // whatever it says, and the family alone still answers as the 9 generation.
    assert_eq!(
        adapter_for_release(&host("ubuntu", DistroFamily::Debian, "10")).family(),
        DistroFamily::Debian
    );
    assert_eq!(
        adapter_for_release(&host("opensuse-leap", DistroFamily::Suse, "10")).nodejs_packaging(),
        adapter_for(DistroFamily::Suse).nodejs_packaging()
    );
    assert_eq!(
        adapter_for(DistroFamily::Rhel).nodejs_packaging(),
        NodejsPackaging::ModuleStreams
    );
}
