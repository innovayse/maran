//! Tests for the pin source that re-reads `panel.pem` on every refresh.
//!
//! The directory and the identity come from the fixture the identity half of `fleet/` is tested
//! with, so the file read here is the one the generator writes.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Arc;

use super::PanelPinFile;
use crate::fleet::fixture_identity::{own_uid, private_directory};
use crate::fleet::pin_source::PinSource;
use crate::fleet::test_certificates::valid_client_certificate;
use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::fleet::certificate_fingerprint::CertificateFingerprint;

/// Writes `pem` as the pin of the node whose directory is `directory`.
fn write_pin(directory: &std::path::Path, pem: &str) {
    std::fs::write(directory.join(AgentPaths::FLEET_PANEL_PIN_FILE), pem).unwrap();
}

#[tokio::test]
async fn a_pin_file_is_read_into_the_pinned_certificate() {
    let temporary = private_directory();
    let directory = temporary.path().to_path_buf();
    let panel = valid_client_certificate("panel");
    write_pin(&directory, &panel.pem);

    let source = PanelPinFile::new(directory, own_uid());
    source.refresh().await;
    let pin = source.current().unwrap();

    assert_eq!(
        *pin.fingerprint(),
        CertificateFingerprint::of_der(&panel.der)
    );
}

#[tokio::test]
async fn no_pin_file_means_nothing_is_pinned() {
    let temporary = private_directory();
    let directory = temporary.path().to_path_buf();

    let source = PanelPinFile::new(directory, own_uid());
    source.refresh().await;

    assert!(source.current().is_none());
}

#[tokio::test]
async fn removing_the_file_takes_the_pin_away_at_the_next_call() {
    let temporary = private_directory();
    let directory = temporary.path().to_path_buf();
    let panel = valid_client_certificate("panel");
    write_pin(&directory, &panel.pem);
    let source = PanelPinFile::new(directory.clone(), own_uid());
    source.refresh().await;
    assert!(
        source.current().is_some(),
        "control: pinned while the file exists"
    );

    std::fs::remove_file(directory.join(AgentPaths::FLEET_PANEL_PIN_FILE)).unwrap();
    source.refresh().await;

    assert!(source.current().is_none());
}

#[tokio::test]
async fn replacing_the_file_changes_the_pin_at_the_next_call() {
    let temporary = private_directory();
    let directory = temporary.path().to_path_buf();
    let first = valid_client_certificate("panel");
    let second = valid_client_certificate("panel");
    write_pin(&directory, &first.pem);
    let source = PanelPinFile::new(directory.clone(), own_uid());
    source.refresh().await;
    assert_eq!(
        *source.current().unwrap().fingerprint(),
        CertificateFingerprint::of_der(&first.der)
    );

    std::fs::remove_file(directory.join(AgentPaths::FLEET_PANEL_PIN_FILE)).unwrap();
    write_pin(&directory, &second.pem);
    source.refresh().await;

    assert_eq!(
        *source.current().unwrap().fingerprint(),
        CertificateFingerprint::of_der(&second.der)
    );
}

#[tokio::test]
async fn a_file_that_is_not_a_certificate_pins_nothing() {
    let temporary = private_directory();
    let directory = temporary.path().to_path_buf();
    write_pin(&directory, "this is not a certificate");
    let source = PanelPinFile::new(directory, own_uid());
    source.refresh().await;

    assert!(source.current().is_none());
}

#[tokio::test]
async fn nothing_is_pinned_until_the_first_refresh() {
    let temporary = private_directory();
    let directory = temporary.path().to_path_buf();
    let panel = valid_client_certificate("panel");
    write_pin(&directory, &panel.pem);
    let source = PanelPinFile::new(directory, own_uid());

    assert!(
        source.current().is_none(),
        "current() answers from memory and never reads the file"
    );
    source.refresh().await;
    assert!(
        source.current().is_some(),
        "control: the same source pins once refreshed"
    );
}

#[tokio::test]
async fn a_pin_file_changed_without_a_refresh_is_not_seen_by_current() {
    let temporary = private_directory();
    let directory = temporary.path().to_path_buf();
    let panel = valid_client_certificate("panel");
    write_pin(&directory, &panel.pem);
    let source = PanelPinFile::new(directory.clone(), own_uid());
    source.refresh().await;

    std::fs::remove_file(directory.join(AgentPaths::FLEET_PANEL_PIN_FILE)).unwrap();

    assert!(
        source.current().is_some(),
        "current() is the copy from the last refresh, which is why every caller refreshes first"
    );
}

#[tokio::test]
async fn concurrent_refreshes_after_the_removal_all_end_with_nothing_pinned() {
    let temporary = private_directory();
    let directory = temporary.path().to_path_buf();
    let panel = valid_client_certificate("panel");
    write_pin(&directory, &panel.pem);
    let source = Arc::new(PanelPinFile::new(directory.clone(), own_uid()));
    source.refresh().await;
    std::fs::remove_file(directory.join(AgentPaths::FLEET_PANEL_PIN_FILE)).unwrap();

    let refreshes: Vec<_> = (0..16)
        .map(|_| {
            let source = Arc::clone(&source);
            tokio::spawn(async move {
                source.refresh().await;
                source.current().is_none()
            })
        })
        .collect();

    for refresh in refreshes {
        assert!(refresh.await.unwrap());
    }
}
