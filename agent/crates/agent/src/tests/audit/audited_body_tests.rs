//! Tests for `audited_body`: frames pass through and the line is written exactly once.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::pin::Pin;

use http_body_util::BodyExt;

use super::AuditedBody;
use crate::audit::audit_emission::AuditEmission;
use crate::audit::audit_slot::AuditSlot;
use crate::audit::line_capture::LineCapture;
use crate::audit::scripted_body::{ScriptedBody, Step};
use crate::proto::ErrorCode;

/// The method entry the tests audit.
const ENTRY: &str = "/maran.agent.v1.DbService/DropDatabase";

/// A body over `steps` audited under a fresh slot, which is returned beside it.
fn audited(
    steps: impl IntoIterator<Item = Step>,
) -> (Pin<Box<AuditedBody<ScriptedBody>>>, AuditSlot) {
    let mut extensions = http::Extensions::new();
    let slot = AuditSlot::install(&mut extensions);
    let emission = AuditEmission::new(ENTRY, slot.clone());
    (
        Box::pin(AuditedBody::new(ScriptedBody::new(steps), Some(emission))),
        slot,
    )
}

#[tokio::test]
async fn end_of_stream_then_drop_writes_one_line() {
    let capture = LineCapture::start();
    let (mut body, slot) = audited([Step::Data("a"), Step::Data("b")]);
    slot.ok();

    while body.frame().await.is_some() {}
    assert_eq!(capture.audit_lines().len(), 1, "the end wrote it");
    drop(body);

    let lines = capture.audit_lines();
    assert_eq!(lines.len(), 1, "the drop wrote nothing more");
    assert!(lines[0].contains("outcome=ok "), "{}", lines[0]);
}

#[tokio::test]
async fn drop_without_reaching_the_end_writes_one_line() {
    let capture = LineCapture::start();
    let (mut body, slot) = audited([Step::Data("a"), Step::Data("b")]);
    slot.refused(ErrorCode::NotFound);

    let _first = body.frame().await;
    assert!(capture.audit_lines().is_empty(), "no line before the end");
    drop(body);

    let lines = capture.audit_lines();
    assert_eq!(lines.len(), 1);
    assert!(
        lines[0].contains("outcome=ERROR_CODE_NOT_FOUND "),
        "{}",
        lines[0]
    );
}

#[tokio::test]
async fn an_inner_error_writes_the_line_and_the_drop_after_it_writes_nothing() {
    let capture = LineCapture::start();
    let (mut body, _slot) = audited([Step::Data("a"), Step::Failure]);

    let _first = body.frame().await;
    let failed = body.frame().await.unwrap();
    assert!(failed.is_err());
    assert_eq!(capture.audit_lines().len(), 1, "the error frame wrote it");
    drop(body);

    assert_eq!(capture.audit_lines().len(), 1);
}

#[tokio::test]
async fn polling_past_the_end_still_writes_one_line() {
    let capture = LineCapture::start();
    let (mut body, _slot) = audited([Step::Data("a")]);

    while body.frame().await.is_some() {}
    assert!(body.frame().await.is_none());
    drop(body);

    assert_eq!(capture.audit_lines().len(), 1);
}

#[tokio::test]
async fn an_unsettled_rpc_reads_unset() {
    let capture = LineCapture::start();
    let (mut body, _slot) = audited([Step::Data("a")]);

    while body.frame().await.is_some() {}

    assert!(capture.audit_lines()[0].contains("outcome=unset "));
}

#[tokio::test]
async fn a_non_ok_grpc_status_in_the_trailers_is_the_transport_outcome_when_unsettled() {
    let capture = LineCapture::start();
    let (mut body, _slot) = audited([Step::Data("a"), Step::Status("7")]);

    while body.frame().await.is_some() {}

    assert!(
        capture.audit_lines()[0].contains("outcome=transport:PermissionDenied "),
        "{:?}",
        capture.audit_lines()
    );
}

#[tokio::test]
async fn an_ok_grpc_status_in_the_trailers_leaves_an_unsettled_rpc_unset() {
    let capture = LineCapture::start();
    let (mut body, _slot) = audited([Step::Data("a"), Step::Status("0")]);

    while body.frame().await.is_some() {}

    assert!(capture.audit_lines()[0].contains("outcome=unset "));
}

#[tokio::test]
async fn a_settled_outcome_wins_over_the_transport_status() {
    let capture = LineCapture::start();
    let (mut body, slot) = audited([Step::Status("13")]);
    slot.refused(ErrorCode::ValidationFailed);

    while body.frame().await.is_some() {}

    assert!(capture.audit_lines()[0].contains("outcome=ERROR_CODE_VALIDATION_FAILED "));
}

#[tokio::test]
async fn an_unrecognised_status_number_is_the_fixed_word_unknown() {
    let capture = LineCapture::start();
    let (mut body, _slot) = audited([Step::Status("4242")]);

    while body.frame().await.is_some() {}

    assert!(capture.audit_lines()[0].contains("outcome=transport:Unknown "));
}

#[tokio::test]
async fn data_frames_pass_through_unchanged() {
    let _capture = LineCapture::start();
    let (mut body, _slot) = audited([Step::Data("hello "), Step::Data("world")]);

    let mut collected = Vec::new();
    while let Some(frame) = body.frame().await {
        collected.extend_from_slice(&frame.unwrap().into_data().unwrap());
    }

    assert_eq!(collected, b"hello world");
}

#[tokio::test]
async fn a_body_without_an_emission_writes_nothing() {
    let capture = LineCapture::start();
    let mut body = Box::pin(AuditedBody::new(
        ScriptedBody::new([Step::Data("a"), Step::Status("7")]),
        None,
    ));

    while body.frame().await.is_some() {}
    drop(body);

    assert!(capture.audit_lines().is_empty());
}
