//! The service that audits mutating rpcs and passes everything else through.

use std::future::Future;
use std::pin::Pin;
use std::task::{Context, Poll};

use http::{Request, Response};
use tower::Service;

use super::audit_emission::AuditEmission;
use super::audit_slot::AuditSlot;
use super::audited_body::AuditedBody;
use super::mutating_rpcs::MUTATING_RPCS;

/// Wraps the tonic router. For a path in `MUTATING_RPCS` it installs a fresh
/// [`AuditSlot`] in the request extensions and wraps the response body so one line is
/// written when the response ends; any other path (read-only or unknown) is forwarded
/// untouched and never printed, so a hostile `:path` cannot reach the journal.
#[derive(Debug, Clone)]
pub struct AuditService<S> {
    /// The service being wrapped.
    inner: S,
}

impl<S> AuditService<S> {
    /// Wraps `inner`.
    #[must_use]
    pub fn new(inner: S) -> Self {
        Self { inner }
    }
}

/// The agent's own list entry equal to the request path, if there is one. The entry,
/// not the request's bytes, is what the line prints.
fn mutating_entry(path: &str) -> Option<&'static str> {
    MUTATING_RPCS.iter().copied().find(|entry| *entry == path)
}

impl<S, ReqBody, ResBody> Service<Request<ReqBody>> for AuditService<S>
where
    S: Service<Request<ReqBody>, Response = Response<ResBody>> + Clone + Send + 'static,
    S::Future: Send + 'static,
    ReqBody: Send + 'static,
    ResBody: Send + 'static,
{
    type Response = Response<AuditedBody<ResBody>>;
    type Error = S::Error;
    type Future = Pin<Box<dyn Future<Output = Result<Self::Response, Self::Error>> + Send>>;

    /// Readiness is the inner service's.
    fn poll_ready(&mut self, cx: &mut Context<'_>) -> Poll<Result<(), Self::Error>> {
        self.inner.poll_ready(cx)
    }

    /// Decides on the path before wrapping anything, forwards the request unchanged
    /// (plus the slot extension for an audited path) and observes the response.
    fn call(&mut self, mut request: Request<ReqBody>) -> Self::Future {
        // The standard tower swap: the clone is the one that was polled ready.
        let clone = self.inner.clone();
        let mut inner = std::mem::replace(&mut self.inner, clone);

        // Created before the inner call so that a future dropped before it answers
        // (the client left) still writes its line from the emission's drop.
        let emission = mutating_entry(request.uri().path()).map(|entry| {
            let slot = AuditSlot::install(request.extensions_mut());
            AuditEmission::new(entry, slot)
        });

        Box::pin(async move {
            let response = inner.call(request).await?;
            let (parts, body) = response.into_parts();
            let emission = emission.map(|mut emission| {
                // A trailers-only response (a refused peer, a failed handler)
                // carries `grpc-status` in its head, not in trailers.
                emission.note_status(&parts.headers);
                emission
            });
            Ok(Response::from_parts(
                parts,
                AuditedBody::new(body, emission),
            ))
        })
    }
}

#[cfg(test)]
#[path = "../tests/audit/audit_service_tests.rs"]
mod tests;
