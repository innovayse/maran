//! Tests for [`NodejsModuleStream`]: built only from a catalogue major, spelled two ways.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::NodejsModuleStream;
use crate::validation::web::node_major::{NodeMajor, SUPPORTED_MAJORS};

#[test]
fn every_catalogue_major_has_a_stream_whose_instance_is_its_digits() {
    for digits in SUPPORTED_MAJORS {
        let stream = NodejsModuleStream::from_major(NodeMajor::parse(digits).unwrap());

        assert_eq!(stream.instance(), digits);
        assert_eq!(stream.major().as_str(), digits);
    }
}

#[test]
fn the_spec_names_the_module_the_stream_and_the_common_profile() {
    let stream = NodejsModuleStream::from_major(NodeMajor::parse("20").unwrap());

    assert_eq!(stream.spec(), "nodejs:20/common");
}

#[test]
fn the_instance_holds_no_character_a_unit_instance_refuses() {
    for digits in SUPPORTED_MAJORS {
        let instance = NodejsModuleStream::from_major(NodeMajor::parse(digits).unwrap()).instance();

        assert!(instance.chars().all(|character| character.is_ascii_digit()));
    }
}

#[test]
fn two_streams_of_one_major_are_equal_and_of_two_majors_are_not() {
    let twenty = NodejsModuleStream::from_major(NodeMajor::parse("20").unwrap());

    assert_eq!(
        twenty,
        NodejsModuleStream::from_major(NodeMajor::parse("20").unwrap())
    );
    assert_ne!(
        twenty,
        NodejsModuleStream::from_major(NodeMajor::parse("22").unwrap())
    );
}
