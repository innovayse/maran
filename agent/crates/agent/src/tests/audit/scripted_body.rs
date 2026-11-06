//! Test support: a response body that plays a fixed list of frames.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::VecDeque;
use std::pin::Pin;
use std::task::{Context, Poll};

use http::{HeaderMap, HeaderValue};
use http_body::{Body, Frame};
use tonic::codegen::Bytes;

/// One scripted step.
pub(crate) enum Step {
    /// A data frame.
    Data(&'static str),
    /// A trailers frame carrying `grpc-status`.
    Status(&'static str),
    /// An error from the inner body.
    Failure,
}

/// A body that yields its steps in order and then ends.
pub(crate) struct ScriptedBody {
    steps: VecDeque<Step>,
}

impl ScriptedBody {
    /// A body that plays `steps`.
    pub(crate) fn new(steps: impl IntoIterator<Item = Step>) -> Self {
        Self {
            steps: steps.into_iter().collect(),
        }
    }
}

impl Body for ScriptedBody {
    type Data = Bytes;
    type Error = String;

    fn poll_frame(
        mut self: Pin<&mut Self>,
        _cx: &mut Context<'_>,
    ) -> Poll<Option<Result<Frame<Bytes>, String>>> {
        Poll::Ready(self.steps.pop_front().map(|step| match step {
            Step::Data(text) => Ok(Frame::data(Bytes::from_static(text.as_bytes()))),
            Step::Status(code) => {
                let mut trailers = HeaderMap::new();
                trailers.insert("grpc-status", HeaderValue::from_static(code));
                Ok(Frame::trailers(trailers))
            }
            Step::Failure => Err("inner body failed".to_owned()),
        }))
    }
}
