//! The three directories a zone's files can be in.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_distro::{DistroFamily, adapter_for};

use super::ZoneDirectories;
use crate::dns::fake_zone_host::{directories, sample_key};
use crate::dns::model::zone_signing::ZoneSigning;

#[test]
fn the_directories_of_a_family_are_its_three_facts() {
    let debian = ZoneDirectories::of(adapter_for(DistroFamily::Debian));
    let rhel = ZoneDirectories::of(adapter_for(DistroFamily::Rhel));

    assert_eq!(debian.zones().to_str(), Some("/var/lib/bind/maran/zones"));
    assert_eq!(
        debian.signed_zones().to_str(),
        Some("/var/lib/bind/maran/signed-zones")
    );
    assert_eq!(debian.keys().to_str(), Some("/var/lib/bind/maran/keys"));
    assert_ne!(debian, rhel);

    // UNVERIFIED: read from the distribution documentation, not measured. The SUSE family keeps
    // its BIND working directory at `/var/lib/named`; confirm with `rpm -ql bind | grep named`
    // in the pinned Leap image.
    let suse = ZoneDirectories::of(adapter_for(DistroFamily::Suse));
    assert_eq!(suse.zones().to_str(), Some("/var/lib/named/maran/zones"));
    assert_eq!(
        suse.signed_zones().to_str(),
        Some("/var/lib/named/maran/signed-zones")
    );
    assert_eq!(suse.keys().to_str(), Some("/var/lib/named/maran/keys"));
    assert_ne!(suse, debian);
    assert_ne!(suse, rhel);
}

#[test]
fn an_unsigned_zone_lives_in_the_plain_directory() {
    let directories = directories();

    assert_eq!(
        directories.directory_for(&ZoneSigning::Unsigned),
        directories.zones()
    );
}

#[test]
fn a_signed_and_a_withdrawing_zone_live_in_the_signed_directory() {
    let directories = directories();

    for signing in [
        ZoneSigning::Signed(sample_key()),
        ZoneSigning::Withdrawing(sample_key()),
    ] {
        assert_eq!(
            directories.directory_for(&signing),
            directories.signed_zones()
        );
    }
}
