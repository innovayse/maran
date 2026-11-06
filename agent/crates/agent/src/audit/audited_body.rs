//! The response body wrapper that decides when the audit line is written.

use std::pin::Pin;
use std::task::{Context, Poll};

use http_body::{Body, Frame, SizeHint};
use pin_project_lite::pin_project;

use super::audit_emission::AuditEmission;

pin_project! {
    /// Wraps a response body and forwards every frame untouched. It reads trailers for
    /// `grpc-status` only, never a data frame, and buffers nothing, so streaming a long
    /// response adds no memory (threat note, section 5).
    ///
    /// With an emission it writes the audit line exactly once: when the inner body ends,
    /// when it yields an error, or when the wrapper is dropped. Without one (a path the
    /// layer does not audit) it is a plain pass-through.
    pub struct AuditedBody<B> {
        #[pin]
        inner: B,
        emission: Option<AuditEmission>,
    }
}

impl<B> AuditedBody<B> {
    /// Wraps `inner`; `emission` is `None` for a path that is not audited.
    #[must_use]
    pub fn new(inner: B, emission: Option<AuditEmission>) -> Self {
        Self { inner, emission }
    }
}

impl<B: Body> Body for AuditedBody<B> {
    type Data = B::Data;
    type Error = B::Error;

    /// Polls the inner body. Trailers are read for the gRPC status; the end of the
    /// stream and an error frame each write the line, once.
    fn poll_frame(
        self: Pin<&mut Self>,
        cx: &mut Context<'_>,
    ) -> Poll<Option<Result<Frame<Self::Data>, Self::Error>>> {
        let this = self.project();
        let polled = this.inner.poll_frame(cx);
        if let Some(emission) = this.emission.as_mut() {
            match &polled {
                Poll::Ready(Some(Ok(frame))) => {
                    if let Some(trailers) = frame.trailers_ref() {
                        emission.note_status(trailers);
                    }
                }
                Poll::Ready(None) | Poll::Ready(Some(Err(_))) => emission.emit(),
                Poll::Pending => {}
            }
        }
        polled
    }

    /// Whether the inner body has ended.
    fn is_end_stream(&self) -> bool {
        self.inner.is_end_stream()
    }

    /// The inner body's size hint, unchanged.
    fn size_hint(&self) -> SizeHint {
        self.inner.size_hint()
    }
}

#[cfg(test)]
#[path = "../tests/audit/audited_body_tests.rs"]
mod tests;
