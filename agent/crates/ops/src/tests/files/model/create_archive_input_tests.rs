//! Tests for the request to create an archive.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::fs::create_bounds::CreateBounds;
use maran_agent_core::validation::system::name::AccountName;

use super::CreateArchiveInput;
use crate::files::model::archive_format::ArchiveFormat;
use crate::files::model::create_archive_input_error::CreateArchiveInputError;

fn build(
    sources: &[&str],
    destination: &str,
) -> Result<CreateArchiveInput, CreateArchiveInputError> {
    CreateArchiveInput::new(
        AccountName::parse("alice").unwrap(),
        sources
            .iter()
            .map(|s| BrowsePath::parse(s).unwrap())
            .collect(),
        BrowsePath::parse(destination).unwrap(),
        ArchiveFormat::Zip,
        CreateBounds::parse(100, 1000).unwrap(),
    )
}

#[test]
fn separate_sources_and_a_destination_beside_them_are_accepted() {
    let input = build(&["sites/a", "sites/b", "notes.txt"], "backup.zip").unwrap();

    assert_eq!(input.sources().len(), 3);
    assert_eq!(input.destination().components(), ["backup.zip"]);
    assert_eq!(input.format(), ArchiveFormat::Zip);
}

#[test]
fn siblings_whose_names_share_a_prefix_do_not_overlap() {
    assert!(build(&["site", "site2"], "out.zip").is_ok());
    assert!(build(&["site"], "site2/out.zip").is_ok());
}

#[test]
fn no_source_is_refused() {
    assert_eq!(
        build(&[], "out.zip").unwrap_err(),
        CreateArchiveInputError::NoSources
    );
}

#[test]
fn more_than_a_thousand_sources_are_refused() {
    let names: Vec<String> = (0..1001).map(|n| format!("s{n}")).collect();
    let sources: Vec<&str> = names.iter().map(String::as_str).collect();

    assert_eq!(
        build(&sources, "out.zip").unwrap_err(),
        CreateArchiveInputError::TooManySources
    );
}

#[test]
fn the_home_as_a_source_or_destination_is_refused() {
    assert_eq!(
        build(&[""], "out.zip").unwrap_err(),
        CreateArchiveInputError::SourceIsHome
    );
    assert_eq!(
        build(&["a"], "").unwrap_err(),
        CreateArchiveInputError::DestinationIsHome
    );
}

#[test]
fn a_destination_inside_a_source_is_refused() {
    assert_eq!(
        build(&["site"], "site/out.zip").unwrap_err(),
        CreateArchiveInputError::DestinationInsideSource
    );
    assert_eq!(
        build(&["site"], "site").unwrap_err(),
        CreateArchiveInputError::DestinationInsideSource
    );
}

#[test]
fn a_source_inside_another_source_is_refused_in_either_order() {
    assert_eq!(
        build(&["site", "site/css"], "out.zip").unwrap_err(),
        CreateArchiveInputError::OverlappingSources
    );
    assert_eq!(
        build(&["site/css", "site"], "out.zip").unwrap_err(),
        CreateArchiveInputError::OverlappingSources
    );
    assert_eq!(
        build(&["site", "site"], "out.zip").unwrap_err(),
        CreateArchiveInputError::OverlappingSources
    );
}
