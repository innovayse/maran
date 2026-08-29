#![warn(missing_docs)]
//! maran-templates — askama render types for the system configs the
//! agent writes (`templates/{nginx,php-fpm,vsftpd,systemd}/`). Rendering
//! follows the safe-write protocol: render → temp file → validate → atomic
//! rename → reload (rules/rust.md "Validation first"). Byte-exact expected
//! renders live in `tests/golden/`. Render types land with Plan 2.
