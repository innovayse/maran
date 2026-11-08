//! Tests for the in-memory upload source.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::BufferedUpload;
use crate::files::model::upload_source::UploadSource;

#[test]
fn the_body_is_handed_out_once_and_then_the_upload_is_complete() {
    let mut upload = BufferedUpload::new(b"body".to_vec());

    assert_eq!(upload.next_chunk().unwrap(), Some(&b"body"[..]));
    assert_eq!(upload.next_chunk().unwrap(), None);
    assert_eq!(upload.next_chunk().unwrap(), None);
}

#[test]
fn an_empty_body_is_complete_at_once() {
    assert_eq!(BufferedUpload::new(Vec::new()).next_chunk().unwrap(), None);
}
