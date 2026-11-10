//! Tests for the `node_major` module: a closed catalogue, so what matters is what it refuses.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::node_major::NodeMajor;
use super::super::node_major_error::NodeMajorError;

#[test]
fn every_catalogue_major_parses() {
    for major in ["18", "20", "22", "24"] {
        assert_eq!(NodeMajor::parse(major).unwrap().as_str(), major);
    }
}

#[test]
fn a_major_outside_the_catalogue_is_rejected() {
    assert!(matches!(
        NodeMajor::parse("19"),
        Err(NodeMajorError::Unsupported { .. })
    ));
}

#[test]
fn a_full_version_is_rejected() {
    assert!(matches!(
        NodeMajor::parse("20.11.1"),
        Err(NodeMajorError::Unsupported { .. })
    ));
}

#[test]
fn a_major_with_a_newline_is_rejected() {
    assert!(matches!(
        NodeMajor::parse("20\nExecStart=/bin/sh"),
        Err(NodeMajorError::Unsupported { .. })
    ));
}

#[test]
fn an_empty_major_is_rejected() {
    assert_eq!(NodeMajor::parse(""), Err(NodeMajorError::Empty));
}
