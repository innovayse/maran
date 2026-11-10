//! The real zone host: the parts of it a test can drive without root.
//!
//! Handing a key file to the daemon's group needs root and that group, so the
//! success path is the polygon suite's (`dns_zone_files_on_a_real_host`); what is
//! driven here is that a group which cannot be resolved stops the write BEFORE any
//! file exists, and the listing.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use crate::dns::dns_error::DnsError;
use crate::dns::process_dns_host::ProcessDnsHost;
use crate::dns::zone_host::ZoneHost;

#[test]
fn a_key_file_for_a_group_that_does_not_exist_is_refused_before_anything_is_written() {
    let host = ProcessDnsHost::new();
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("Kexample.com.+013+12345.private");

    let refused = host
        .write_key_file(&target, "secret\n", "no-such-group-for-maran-tests")
        .unwrap_err();

    assert!(matches!(refused, DnsError::ConfigWrite { .. }));
    assert!(!refused.to_string().contains("secret"));
    assert_eq!(std::fs::read_dir(directory.path()).unwrap().count(), 0);
}

#[test]
fn a_directory_that_is_not_there_lists_as_empty() {
    let host = ProcessDnsHost::new();

    assert!(
        host.zone_file_names(Path::new("/nonexistent/maran-dns/keys"))
            .unwrap()
            .is_empty()
    );
}

#[test]
fn the_names_in_a_directory_are_listed() {
    let host = ProcessDnsHost::new();
    let directory = tempfile::tempdir().unwrap();
    std::fs::write(directory.path().join("a.example.db"), "x").unwrap();

    assert_eq!(
        host.zone_file_names(directory.path()).unwrap(),
        ["a.example.db"]
    );
}
