//! The proto ↔ domain boundary, shared by every service.
//!
//! One home for the things every service repeats at its edge: turning a
//! rejected input into the wire error (`invalid_input`), revalidating the
//! account name an rpc carries (`validated_account`), handing a blocking
//! operation to the runtime (`run_blocking`), reporting an agent-side
//! breakdown (`system_failure`), and sending into a response stream's channel from a
//! blocking thread under a deadline (`deadline_sender`, with its clock and its ending), and the
//! bounded forwarding of a client-streamed rpc's body to its blocking operation (`stream_pump`, with
//! `chunk_collector`, `pump_message`, `chunk_receiver` and `stream_ended`). Services import from here and never from
//! each other's folders.

pub mod chunk_collector;
pub mod chunk_receiver;
pub mod deadline_sender;
pub mod invalid_input;
pub mod pump_message;
pub mod run_blocking;
pub mod send_end;
pub mod sink_clock;
pub mod stalled_upload;
pub mod stream_ended;
pub mod stream_pump;
pub mod system_failure;
pub mod system_sink_clock;
pub mod validated_account;
