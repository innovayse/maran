//! How a mutating rpc ended, as the audit line spells it.

use std::fmt;

use tonic::Code;

use crate::proto::ErrorCode;

/// The outcome field of an audit line: a closed set, so the text it renders to can
/// never carry a caller's string (threat note, section 2).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AuditOutcome {
    /// The handler settled the rpc as a success.
    Ok,
    /// The handler settled the rpc as a typed refusal; the code is the proto's own.
    Refused(ErrorCode),
    /// No handler settled the rpc and the transport reported no failure either: the
    /// agent was asked and never said how it ended (a client that left, a gap in the
    /// wiring). It is NOT a claim that nothing happened.
    Unset,
    /// No handler settled the rpc and the gRPC status the response ended with was not OK
    /// (a peer the guard refused, a panic inside a handler turned into a status).
    Transport(Code),
}

impl fmt::Display for AuditOutcome {
    /// Writes `ok`, the proto name of the error code, `unset` or `transport:<name>`.
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Ok => formatter.write_str("ok"),
            Self::Refused(code) => formatter.write_str(code.as_str_name()),
            Self::Unset => formatter.write_str("unset"),
            Self::Transport(code) => write!(formatter, "transport:{}", code_name(*code)),
        }
    }
}

/// The fixed name of a gRPC status code. A closed table: the text never comes from
/// the wire, so a hostile `grpc-status` value cannot reach the line.
fn code_name(code: Code) -> &'static str {
    match code {
        Code::Ok => "Ok",
        Code::Cancelled => "Cancelled",
        Code::Unknown => "Unknown",
        Code::InvalidArgument => "InvalidArgument",
        Code::DeadlineExceeded => "DeadlineExceeded",
        Code::NotFound => "NotFound",
        Code::AlreadyExists => "AlreadyExists",
        Code::PermissionDenied => "PermissionDenied",
        Code::ResourceExhausted => "ResourceExhausted",
        Code::FailedPrecondition => "FailedPrecondition",
        Code::Aborted => "Aborted",
        Code::OutOfRange => "OutOfRange",
        Code::Unimplemented => "Unimplemented",
        Code::Internal => "Internal",
        Code::Unavailable => "Unavailable",
        Code::DataLoss => "DataLoss",
        Code::Unauthenticated => "Unauthenticated",
    }
}

#[cfg(test)]
#[path = "../tests/audit/audit_outcome_tests.rs"]
mod tests;
