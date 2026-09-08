//! Values that must never appear in logs or errors: passwords, validated to be
//! safe for interpolation into root SQL and `chpasswd` lines. A value that only
//! needs to be hidden, not validated, is a [`crate::secret_string::SecretString`]
//! instead — it lives at the crate root because it validates nothing.

pub mod password;
pub mod password_error;
