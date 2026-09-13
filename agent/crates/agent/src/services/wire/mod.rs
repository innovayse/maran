//! The proto ↔ domain boundary, shared by every service.
//!
//! One home for the things every service repeats at its edge: turning a
//! rejected input into the wire error (`invalid_input`), revalidating the
//! account name an rpc carries (`validated_account`), handing a blocking
//! operation to the runtime (`run_blocking`), and reporting an agent-side
//! breakdown (`system_failure`). Services import from here and never from
//! each other's folders.

pub mod invalid_input;
pub mod run_blocking;
pub mod system_failure;
pub mod validated_account;
