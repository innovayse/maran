#![warn(missing_docs)]
//! maran-ops — the agent's domain operations, one module per area
//! (`accounts/`, `sites/`, `php/`, `db/`, `ftp/`, `files/`, `cron/`,
//! `firewall/`, `ssl/`, `backup/`, `monitor/`). Every operation is
//! idempotent and re-validates its inputs via `maran-agent-core`
//! (rules/rust.md "Validation first"). Modules land with their backend
//! counterparts, starting with Plan 2.
