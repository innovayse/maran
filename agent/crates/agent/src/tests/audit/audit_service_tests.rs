//! Tests for `audit_service`: which paths are audited, and that the request and the
//! response pass through unchanged.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::convert::Infallible;
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};

use http::{Request, Response};
use http_body_util::BodyExt;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;
use tower::{Layer, Service, ServiceExt, service_fn};

use crate::audit::audit_layer::AuditLayer;
use crate::audit::audit_slot::AuditSlot;
use crate::audit::line_capture::LineCapture;
use crate::audit::mutating_rpcs::MUTATING_RPCS;
use crate::audit::read_only_rpcs::READ_ONLY_RPCS;
use crate::audit::scripted_body::{ScriptedBody, Step};
use crate::proto::ErrorCode;

/// A request for `path` with no body.
fn request(path: &str) -> Request<()> {
    Request::builder().uri(path).body(()).unwrap()
}

#[tokio::test]
async fn a_mutating_path_writes_one_line_when_the_response_ends() {
    let capture = LineCapture::start();
    assert!(
        !MUTATING_RPCS.is_empty(),
        "positive control: the list has paths"
    );
    let path = MUTATING_RPCS[0];
    let mut service = AuditLayer.layer(service_fn(|request: Request<()>| async move {
        let audit = AuditSlot::of(request.extensions());
        audit.account(&AccountName::parse("alice_01").unwrap());
        audit.site(&Domain::parse("example.com").unwrap());
        audit.ok();
        Ok::<_, Infallible>(Response::new(ScriptedBody::new([Step::Data("x")])))
    }));

    let response = service
        .ready()
        .await
        .unwrap()
        .call(request(path))
        .await
        .unwrap();
    assert!(
        capture.audit_lines().is_empty(),
        "nothing before the response ends"
    );
    let mut body = response.into_body();
    while body.frame().await.is_some() {}
    drop(body);

    let lines = capture.audit_lines();
    assert_eq!(lines.len(), 1, "{lines:?}");
    assert!(
        lines[0].starts_with(&format!("event=rpc method={} ", &path[1..])),
        "{}",
        lines[0]
    );
    assert!(lines[0].contains(" outcome=ok "), "{}", lines[0]);
    assert!(
        lines[0].ends_with(" account=alice_01 site=example.com"),
        "{}",
        lines[0]
    );
}

#[tokio::test]
async fn a_refusal_the_handler_settled_is_the_outcome() {
    let capture = LineCapture::start();
    let path = MUTATING_RPCS[0];
    let mut service = AuditLayer.layer(service_fn(|request: Request<()>| async move {
        AuditSlot::of(request.extensions()).refused(ErrorCode::NotFound);
        Ok::<_, Infallible>(Response::new(ScriptedBody::new([Step::Data("x")])))
    }));

    let response = service
        .ready()
        .await
        .unwrap()
        .call(request(path))
        .await
        .unwrap();
    let mut body = response.into_body();
    while body.frame().await.is_some() {}

    assert!(capture.audit_lines()[0].contains(" outcome=ERROR_CODE_NOT_FOUND "));
}

#[tokio::test]
async fn a_read_only_path_writes_nothing_and_gets_no_slot() {
    let capture = LineCapture::start();
    assert!(
        !READ_ONLY_RPCS.is_empty(),
        "positive control: the list has paths"
    );
    let path = READ_ONLY_RPCS[0];
    let had_slot = Arc::new(AtomicBool::new(true));
    let seen = Arc::clone(&had_slot);
    let mut service = AuditLayer.layer(service_fn(move |request: Request<()>| {
        let seen = Arc::clone(&seen);
        async move {
            seen.store(
                request.extensions().get::<AuditSlot>().is_some(),
                Ordering::SeqCst,
            );
            Ok::<_, Infallible>(Response::new(ScriptedBody::new([Step::Data("x")])))
        }
    }));

    let response = service
        .ready()
        .await
        .unwrap()
        .call(request(path))
        .await
        .unwrap();
    let mut body = response.into_body();
    while body.frame().await.is_some() {}
    drop(body);

    assert!(capture.audit_lines().is_empty());
    assert!(!had_slot.load(Ordering::SeqCst));
}

#[tokio::test]
async fn an_unknown_path_writes_nothing_and_is_forwarded_unchanged() {
    let capture = LineCapture::start();
    let hostile = "/maran.agent.v1.Nope/Forged%0Aevent=rpc";
    let seen_path = Arc::new(std::sync::Mutex::new(String::new()));
    let record = Arc::clone(&seen_path);
    let mut service = AuditLayer.layer(service_fn(move |request: Request<()>| {
        let record = Arc::clone(&record);
        async move {
            *record.lock().unwrap() = request.uri().path().to_owned();
            Ok::<_, Infallible>(Response::new(ScriptedBody::new([])))
        }
    }));

    let response = service
        .ready()
        .await
        .unwrap()
        .call(request(hostile))
        .await
        .unwrap();
    let mut body = response.into_body();
    while body.frame().await.is_some() {}
    drop(body);

    assert!(capture.audit_lines().is_empty());
    assert_eq!(*seen_path.lock().unwrap(), hostile);
}

#[tokio::test]
async fn a_trailers_only_refusal_in_the_head_is_the_transport_outcome() {
    let capture = LineCapture::start();
    let path = MUTATING_RPCS[0];
    let mut service = AuditLayer.layer(service_fn(|_request: Request<()>| async move {
        let mut response = Response::new(ScriptedBody::new([]));
        response
            .headers_mut()
            .insert("grpc-status", http::HeaderValue::from_static("7"));
        Ok::<_, Infallible>(response)
    }));

    let response = service
        .ready()
        .await
        .unwrap()
        .call(request(path))
        .await
        .unwrap();
    drop(response);

    let lines = capture.audit_lines();
    assert_eq!(lines.len(), 1, "{lines:?}");
    assert!(
        lines[0].contains(" outcome=transport:PermissionDenied "),
        "{}",
        lines[0]
    );
    assert!(lines[0].ends_with(" account=- site=-"));
}

#[tokio::test]
async fn a_response_dropped_before_its_body_is_read_writes_one_line() {
    let capture = LineCapture::start();
    let path = MUTATING_RPCS[0];
    let mut service = AuditLayer.layer(service_fn(|_request: Request<()>| async move {
        Ok::<_, Infallible>(Response::new(ScriptedBody::new([Step::Data("x")])))
    }));

    let response = service
        .ready()
        .await
        .unwrap()
        .call(request(path))
        .await
        .unwrap();
    drop(response);

    let lines = capture.audit_lines();
    assert_eq!(lines.len(), 1, "{lines:?}");
    assert!(lines[0].contains(" outcome=unset "), "{}", lines[0]);
}

#[tokio::test]
async fn a_call_dropped_before_the_handler_answers_writes_one_line() {
    let capture = LineCapture::start();
    let path = MUTATING_RPCS[0];
    let mut service = AuditLayer.layer(service_fn(|_request: Request<()>| async move {
        std::future::pending::<()>().await;
        Ok::<_, Infallible>(Response::new(ScriptedBody::new([])))
    }));

    let pending = service.ready().await.unwrap().call(request(path));
    drop(pending);

    let lines = capture.audit_lines();
    assert_eq!(lines.len(), 1, "{lines:?}");
    assert!(lines[0].contains(" outcome=unset "), "{}", lines[0]);
}

#[tokio::test]
async fn every_mutating_path_is_audited_and_prints_its_own_list_entry() {
    let capture = LineCapture::start();
    let mut service = AuditLayer.layer(service_fn(|_request: Request<()>| async move {
        Ok::<_, Infallible>(Response::new(ScriptedBody::new([])))
    }));

    for path in MUTATING_RPCS {
        let response = service
            .ready()
            .await
            .unwrap()
            .call(request(path))
            .await
            .unwrap();
        drop(response);
    }

    let lines = capture.audit_lines();
    assert_eq!(lines.len(), MUTATING_RPCS.len());
    for (line, path) in lines.iter().zip(MUTATING_RPCS) {
        assert!(
            line.starts_with(&format!("event=rpc method={} ", &path[1..])),
            "{line}"
        );
    }
}
