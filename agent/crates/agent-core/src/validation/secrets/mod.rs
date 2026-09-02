//! Values that must never appear in logs or errors: passwords and the
//! redacting wrapper that keeps any secret out of Debug/Display output.

pub mod password;
pub mod password_error;
pub mod secret;
