//! The S3 transport: its request shape, its redaction, and its three-state
//! verdict.
//!
//! # What these tests run against, and the limit that comes with it
//!
//! Every test here that speaks HTTP speaks it to [`S3Stub`] — a local server in
//! this repository that answers the status code the test told it to answer.
//! That stub proves two things, and they are both ours:
//!
//! - **the request shape**: which method goes to which path, and above all
//!   whether an `Authorization` header was on it. The public-read probe is a
//!   claim about what an ANONYMOUS reader gets, and a probe that quietly signed
//!   its request would report `Private` about a bucket the whole internet can
//!   read. The stub can see that header, so a test can assert its absence.
//! - **the translation**: a 200 becomes `PubliclyReadable`, a 403/404 becomes
//!   `Private`, a connection that was never made becomes `Unproven`, and a
//!   provider's error text becomes a redacted `ObjectStoreFailed`.
//!
//! **It proves NOTHING about a real provider's policy semantics**, and no
//! reader of this file should come away thinking otherwise. Whether a given
//! provider answers an unauthenticated GET of a private object with 403 or with
//! 404, whether a bucket-level public-access block outranks an object ACL,
//! whether a CDN in front of the bucket serves the object anyway — a stub
//! cannot answer any of it, because a stub is this product's own idea of what a
//! provider does, and the entire reason the probe exists is that this product's
//! idea of a bucket's policy is not evidence about the bucket. Those answers
//! come from the live pass against real providers, which is a separate task and
//! is the only thing that can say the probe WORKS. Green here says the probe is
//! built right.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::net::TcpListener;

use tempfile::TempDir;
use tokio::runtime::{Builder, Runtime};

use super::*;

#[path = "s3_stub.rs"]
mod s3_stub;

use s3_stub::S3Stub;

/// The access key id every test configures. Long enough to be a credential.
const ACCESS_KEY_ID: &str = "AKIAIOSFODNN7EXAMPLE";

/// The secret every test configures.
const SECRET_ACCESS_KEY: &str = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";

/// The bucket every test configures.
const BUCKET: &str = "maran-backups";

/// A runtime the blocking seam can be driven on.
///
/// Multi-threaded, because the host's methods are called from an ordinary
/// thread here (a test is not inside the runtime), and the IO driver has to be
/// turned by somebody while `Handle::block_on` waits.
fn runtime() -> Runtime {
    Builder::new_multi_thread()
        .enable_all()
        .build()
        .expect("a tokio runtime")
}

/// The bucket name, parsed.
fn bucket() -> S3Bucket {
    S3Bucket::parse(BUCKET).expect("the fixture bucket is valid")
}

/// The region, parsed.
fn region() -> S3Region {
    S3Region::parse("eu-central-1").expect("the fixture region is valid")
}

/// A host pointed at `endpoint`, through the private constructor that has
/// already had the scheme decision made for it.
///
/// The transport tests have to speak plain HTTP to a local stub, and they enter
/// through `over_endpoint` rather than weakening the HTTPS check they are not
/// testing. That check keeps its own test — and its inverse control — below.
fn host_at(runtime: &Runtime, endpoint: &str) -> S3ObjectStoreHost {
    S3ObjectStoreHost::over_endpoint(
        runtime.handle().clone(),
        endpoint,
        &bucket(),
        &region(),
        SecretString::new(ACCESS_KEY_ID.to_owned()),
        &SecretString::new(SECRET_ACCESS_KEY.to_owned()),
    )
    .expect("the fixture host builds")
}

/// A port nothing is listening on.
fn closed_port() -> u16 {
    let listener = TcpListener::bind("127.0.0.1:0").expect("a loopback port");
    let port = listener.local_addr().expect("the bound address").port();
    drop(listener);
    port
}

/// A file of `bytes` length, for an upload.
fn file_of(directory: &TempDir, name: &str, bytes: usize) -> std::path::PathBuf {
    let path = directory.path().join(name);
    let mut file = std::fs::File::create(&path).expect("the fixture file is creatable");
    file.write_all(&vec![b'm'; bytes])
        .expect("the fixture file is writable");
    path
}

/// **The refusal.** An `http://` endpoint never becomes a host: what crosses
/// that wire is a customer's whole home and a signature good for the bucket.
#[test]
fn an_http_endpoint_is_refused() {
    let runtime = runtime();

    let refusal = S3ObjectStoreHost::new(
        runtime.handle().clone(),
        "http://storage.example.com",
        &bucket(),
        &region(),
        SecretString::new(ACCESS_KEY_ID.to_owned()),
        &SecretString::new(SECRET_ACCESS_KEY.to_owned()),
    );

    assert!(matches!(refusal, Err(BackupError::DestinationInsecure)));
}

/// **The inverse control.** A gate mutated to refuse everything passes every
/// test that only ever hands it bad input, so this hands it good input: a
/// custom HTTPS endpoint — how every S3-compatible provider that is not AWS is
/// reached — is ACCEPTED, and so is an empty endpoint.
#[test]
fn an_https_endpoint_and_an_absent_endpoint_are_both_accepted() {
    let runtime = runtime();

    for endpoint in ["https://storage.example.com", ""] {
        let built = S3ObjectStoreHost::new(
            runtime.handle().clone(),
            endpoint,
            &bucket(),
            &region(),
            SecretString::new(ACCESS_KEY_ID.to_owned()),
            &SecretString::new(SECRET_ACCESS_KEY.to_owned()),
        );

        assert!(built.is_ok(), "{endpoint} was refused");
    }
}

/// The host is formatted the way a log line formats it, and the output is
/// grepped for the credentials it holds.
///
/// A derived `Debug` on a struct holding a credential is how credentials
/// actually reach logs — nobody prints the key, they print the struct — and one
/// of this struct's fields is a client from another crate whose own `Debug`
/// this code does not control.
#[test]
fn the_hosts_debug_output_contains_no_credential() {
    let runtime = runtime();
    let host = host_at(&runtime, "");

    let formatted = format!("{host:?}");

    assert!(
        !formatted.contains(ACCESS_KEY_ID),
        "the access key id is in {formatted}"
    );
    assert!(
        !formatted.contains(SECRET_ACCESS_KEY),
        "the secret access key is in {formatted}"
    );
    // The inverse control: the formatter is not simply empty. It still says
    // WHICH destination a log line is about, which is the whole reason it
    // exists.
    assert!(formatted.contains(BUCKET), "no destination in {formatted}");
}

/// A provider's error quoting BOTH the access key id and a signature comes back
/// with neither.
///
/// Both, in one message, because that is the message a provider really sends:
/// the id travels inside the `Credential=` scope and the signature travels as a
/// query parameter of the presigned URL the error echoes.
#[test]
fn an_error_message_quoting_a_signature_is_redacted() {
    let key = SecretString::new(ACCESS_KEY_ID.to_owned());
    let message = format!(
        "SignatureDoesNotMatch: request \
         https://maran-backups.s3.eu-central-1.amazonaws.com/backups/alice/x.tar.gz\
         ?X-Amz-Credential={ACCESS_KEY_ID}%2F20260905%2Feu-central-1%2Fs3%2Faws4_request\
         &X-Amz-Signature=5d672d79c15b13162d9279b0855cfba6789a8edb4c82c400e06b5924a6f2b5d7\
         &X-Amz-Date=20260905T000000Z failed"
    );

    let redacted = redact(&key, &message);

    assert!(
        !redacted.contains(ACCESS_KEY_ID),
        "the access key id survived: {redacted}"
    );
    assert!(
        !redacted.contains("5d672d79c15b13162d9279b0855cfba6789a8edb4c82c400e06b5924a6f2b5d7"),
        "the signature survived: {redacted}"
    );
    // The inverse control: it redacts, it does not erase. An operator still
    // gets the condition, the bucket and the fact that a presigned URL was
    // involved.
    assert!(redacted.contains("SignatureDoesNotMatch"), "{redacted}");
    assert!(
        redacted.contains("X-Amz-Signature=«redacted»"),
        "{redacted}"
    );
    assert!(
        redacted.contains("X-Amz-Date=20260905T000000Z"),
        "{redacted}"
    );
}

/// A bucket that hands the object to an unauthenticated caller is reported as
/// publicly readable, and the request that established it carried no
/// credentials.
///
/// The second assertion is the one that keeps the first honest: a probe made
/// with the signed client would answer `Private` about a bucket the internet
/// can read, and only the request line can tell the two apart.
#[test]
fn a_public_bucket_probe_reports_publicly_readable() {
    let runtime = runtime();
    let stub = S3Stub::start(200);
    stub.insert("/maran-backups/probe/canary", b"anyone can read this");
    let host = host_at(&runtime, stub.endpoint());

    let verdict = host
        .probe_public_read("probe/canary")
        .expect("the probe answers");

    assert_eq!(verdict, PublicReadVerdict::PubliclyReadable);
    assert!(!verdict.destination_is_acceptable());
    assert_eq!(
        stub.requests(),
        vec!["GET /maran-backups/probe/canary auth=no".to_owned()],
        "the probe must be unauthenticated"
    );
}

/// **The inverse control for the verdict.** A bucket that refuses the
/// unauthenticated caller is reported private and may be saved.
#[test]
fn a_bucket_that_refuses_the_anonymous_reader_reports_private() {
    let runtime = runtime();

    for status in [403_u16, 404, 401] {
        let stub = S3Stub::start(status);
        stub.insert("/maran-backups/probe/canary", b"private");
        let host = host_at(&runtime, stub.endpoint());

        let verdict = host
            .probe_public_read("probe/canary")
            .expect("the probe answers");

        assert_eq!(verdict, PublicReadVerdict::Private, "on {status}");
        assert!(verdict.destination_is_acceptable(), "on {status}");
    }
}

/// A probe whose GET never reached anything reports `Unproven`, and the panel
/// therefore refuses the destination.
///
/// This is the test the three-state verdict exists for. "The anonymous GET
/// could not be made" is not "the object is private", and a boolean has nowhere
/// to put it — so it would be reported as private, which is a check certifying
/// the absence of a defect it never looked for (rules/testing.md).
#[test]
fn a_probe_that_cannot_reach_the_endpoint_reports_unproven_and_never_private() {
    let runtime = runtime();
    let host = host_at(&runtime, &format!("http://127.0.0.1:{}", closed_port()));

    let verdict = host
        .probe_public_read("probe/canary")
        .expect("an unreachable endpoint is a verdict, not an error");

    assert!(
        matches!(verdict, PublicReadVerdict::Unproven { .. }),
        "an unreachable endpoint answered {verdict:?}"
    );
    assert_ne!(verdict, PublicReadVerdict::Private);
    assert!(!verdict.destination_is_acceptable());
}

/// A provider that answers a server error has said nothing about its policy, so
/// the verdict is `Unproven` rather than `Private`.
#[test]
fn a_provider_that_answers_a_server_error_reports_unproven() {
    let runtime = runtime();
    let stub = S3Stub::start(500);
    stub.insert("/maran-backups/probe/canary", b"unknown");
    let host = host_at(&runtime, stub.endpoint());

    let verdict = host
        .probe_public_read("probe/canary")
        .expect("the probe answers");

    assert!(
        matches!(verdict, PublicReadVerdict::Unproven { .. }),
        "a 500 answered {verdict:?}"
    );
}

/// An unproven verdict's reason carries no credential, because it is built from
/// the provider's own words.
#[test]
fn an_unproven_verdicts_reason_carries_no_credential() {
    let runtime = runtime();
    let host = host_at(&runtime, &format!("http://127.0.0.1:{}", closed_port()));

    let verdict = host.probe_public_read("probe/canary").expect("a verdict");

    let PublicReadVerdict::Unproven { reason } = verdict else {
        panic!("expected an unproven verdict");
    };
    assert!(!reason.contains(ACCESS_KEY_ID), "{reason}");
    assert!(!reason.contains(SECRET_ACCESS_KEY), "{reason}");
    assert!(!reason.is_empty(), "an unproven verdict must say why");
}

/// The signed client puts, heads, gets, lists and deletes — and every one of
/// those requests carries an `Authorization` header.
///
/// One test for the whole round trip because it is one claim: this is the shape
/// of the traffic this product sends. Splitting it into five would repeat the
/// setup five times to assert five lines of one recording.
#[test]
fn an_object_round_trips_through_the_signed_client() {
    let runtime = runtime();
    let stub = S3Stub::start(200);
    let host = host_at(&runtime, stub.endpoint());
    let directory = TempDir::new().expect("a temporary directory");
    let source = file_of(&directory, "artifact.tar.gz", 4096);
    let mut sink = RecordingSink::default();

    let sent = host
        .put("backups/alice/one.tar.gz", &source, &mut sink)
        .expect("the upload succeeds");
    assert_eq!(sent, 4096);
    assert!(stub.holds("/maran-backups/backups/alice/one.tar.gz"));

    let into = directory.path().join("fetched.tar.gz");
    let received = host
        .get("backups/alice/one.tar.gz", &into)
        .expect("the download succeeds");
    assert_eq!(received, 4096);
    assert_eq!(
        std::fs::read(&into)
            .expect("the fetched file is readable")
            .len(),
        4096
    );

    let listed = host.list("backups/alice/").expect("the listing succeeds");
    assert_eq!(
        listed,
        vec![ObjectSummary {
            key: "backups/alice/one.tar.gz".to_owned(),
            bytes: 4096,
        }]
    );

    host.delete("backups/alice/one.tar.gz")
        .expect("the delete succeeds");
    assert!(!stub.holds("/maran-backups/backups/alice/one.tar.gz"));

    let unsigned: Vec<String> = stub
        .requests()
        .into_iter()
        .filter(|line| line.ends_with("auth=no"))
        .collect();
    assert!(
        unsigned.is_empty(),
        "these requests went out unsigned: {unsigned:?}"
    );
}

/// Deleting an object the bucket does not hold is success: the operation is
/// idempotent, and a retry of a delete whose response was lost must not fail.
#[test]
fn deleting_an_object_the_bucket_does_not_hold_reports_success() {
    let runtime = runtime();
    let stub = S3Stub::start(200);
    let host = host_at(&runtime, stub.endpoint());

    let outcome = host.delete("backups/alice/absent.tar.gz");

    assert_eq!(outcome, Ok(()));
}

/// Fetching an object the bucket does not hold is `ObjectNotFound` — its own
/// variant, because a caller branches on it.
#[test]
fn fetching_an_object_the_bucket_does_not_hold_reports_not_found() {
    let runtime = runtime();
    let stub = S3Stub::start(200);
    let host = host_at(&runtime, stub.endpoint());
    let directory = TempDir::new().expect("a temporary directory");

    let refusal = host.get(
        "backups/alice/absent.tar.gz",
        &directory.path().join("nothing"),
    );

    assert_eq!(refusal, Err(BackupError::ObjectNotFound));
}

/// An upload's progress is computed from the bytes it sent, and the terminal
/// report is the stage's own end — never a literal in between.
#[test]
fn upload_progress_is_computed_from_bytes_sent() {
    let runtime = runtime();
    let stub = S3Stub::start(200);
    let host = host_at(&runtime, stub.endpoint());
    let directory = TempDir::new().expect("a temporary directory");
    let source = file_of(&directory, "artifact.tar.gz", 1024);
    let mut sink = RecordingSink::default();

    host.put("backups/alice/one.tar.gz", &source, &mut sink)
        .expect("the upload succeeds");

    assert_eq!(
        sink.reports,
        vec![(BackupStage::Uploading, BackupStage::Uploading.end_percent())]
    );
}

/// A sink that keeps what it was told.
#[derive(Default)]
struct RecordingSink {
    /// Every report, in order.
    reports: Vec<(BackupStage, u32)>,
}

impl ProgressSink for RecordingSink {
    /// Records the report.
    fn report(&mut self, stage: BackupStage, percent: u32) {
        self.reports.push((stage, percent));
    }
}
