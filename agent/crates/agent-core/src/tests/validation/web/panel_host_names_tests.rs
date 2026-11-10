//! Tests for the `panel_host_names` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::domain::Domain;
use super::super::domain_error::DomainError;
use super::PanelHostNames;

fn domain(name: &str) -> Domain {
    Domain::parse(name).unwrap()
}

#[test]
fn a_name_in_the_list_is_refused() {
    let names = PanelHostNames::parse_list("panel.example.com").unwrap();

    assert_eq!(
        names.check(domain("panel.example.com")),
        Err(DomainError::PanelHostName)
    );
}

#[test]
fn every_name_in_the_list_is_refused_not_only_the_first() {
    let names = PanelHostNames::parse_list("srv1.provider.net,panel.example.com").unwrap();

    assert!(names.check(domain("srv1.provider.net")).is_err());
    assert!(names.check(domain("panel.example.com")).is_err());
}

#[test]
fn the_www_form_of_a_listed_name_is_refused() {
    let names = PanelHostNames::parse_list("panel.example.com").unwrap();

    assert_eq!(
        names.check(domain("www.panel.example.com")),
        Err(DomainError::PanelHostName)
    );
}

#[test]
fn a_listed_name_is_refused_whatever_its_case() {
    let names = PanelHostNames::parse_list("Panel.Example.COM").unwrap();

    assert!(names.check(domain("PANEL.example.com")).is_err());
}

#[test]
fn an_unrelated_domain_is_accepted() {
    let names = PanelHostNames::parse_list("srv1.provider.net,panel.example.com").unwrap();

    assert_eq!(
        names.check(domain("shop.example.com")).unwrap().as_str(),
        "shop.example.com"
    );
}

#[test]
fn a_name_under_a_listed_name_is_accepted() {
    let names = PanelHostNames::parse_list("panel.example.com").unwrap();

    assert!(names.check(domain("x.panel.example.com")).is_ok());
}

#[test]
fn the_parent_domain_of_a_listed_name_is_accepted() {
    let names = PanelHostNames::parse_list("panel.example.com").unwrap();

    assert!(names.check(domain("example.com")).is_ok());
}

#[test]
fn an_empty_list_is_none_and_refuses_nothing() {
    let names = PanelHostNames::parse_list("").unwrap();

    assert_eq!(names, PanelHostNames::none());
    assert!(names.check(domain("panel.example.com")).is_ok());
}

#[test]
fn a_list_with_an_empty_entry_is_refused() {
    assert_eq!(
        PanelHostNames::parse_list("panel.example.com,"),
        Err(DomainError::Empty)
    );
}

#[test]
fn a_list_entry_that_is_not_a_host_name_is_refused() {
    assert!(matches!(
        PanelHostNames::parse_list("panel.example.com;}"),
        Err(DomainError::IllegalCharacter { .. })
    ));
}
