//! The audited steps every mutating customer-file rpc shares.

use std::sync::Arc;

use maran_agent_core::validation::system::name::AccountName;
use maran_ops::files::FilesOpError;
use tonic::{Request, Response};

use crate::audit::audit_slot::AuditSlot;
use crate::proto::AgentError;
use crate::services::files::file_status::to_agent_error;
use crate::services::wire::run_blocking::run_blocking;

/// Serves one mutating rpc: takes the audit slot, revalidates the request, names
/// the account on the slot, runs `operation` on the blocking pool, settles the
/// slot and builds the response.
///
/// The bookkeeping `rules/rust.md` ("Service anatomy") asks of a mutating rpc,
/// written once so that the handlers cannot settle it differently from one
/// another: the slot is taken before the request is consumed, the account is
/// named as soon as the validated input exists, and the slot is settled with the
/// same `Result` that becomes the response. A request refused at validation is
/// settled too, as the refusal, so the journal never shows `outcome=unset` for a
/// request that was merely invalid. Nothing here branches on a business
/// condition and nothing touches the filesystem; each handler still states, in
/// its own doc comment, which cancellation class its rpc is.
///
/// `validated` is the request's `validated_*` step; a request that fails it
/// never reaches the blocking pool. `respond` is the `<verb>ed_<noun>` builder
/// that puts the `Result` into the response's `oneof`: failures travel in the
/// payload, never as a gRPC status (rules/proto.md).
///
/// Infallible: every failure is carried in the response built by `respond`, so
/// the handler that calls this wraps the answer in `Ok`.
pub async fn run_file_operation<H, R, I, T, P>(
    request: Request<R>,
    host: &Arc<H>,
    validated: impl FnOnce(&R) -> Result<I, AgentError>,
    account_of: impl FnOnce(&I) -> &AccountName,
    operation: impl FnOnce(&H, &I) -> Result<T, FilesOpError> + Send + 'static,
    respond: impl FnOnce(Result<T, AgentError>) -> P,
) -> Response<P>
where
    H: Send + Sync + 'static,
    I: Send + 'static,
    T: Send + 'static,
{
    let audit = AuditSlot::of(request.extensions());
    let result = match validated(&request.into_inner()) {
        Ok(input) => {
            audit.account(account_of(&input));
            let host = Arc::clone(host);
            run_blocking("file operation", to_agent_error, move || {
                operation(host.as_ref(), &input)
            })
            .await
        }
        Err(error) => Err(error),
    };
    audit.settle(&result);

    Response::new(respond(result))
}
