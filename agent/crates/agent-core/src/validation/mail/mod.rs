//! Values written into the mail stack's configuration and data: a hosted
//! domain, the parts of an address, the limits of a mailbox, the DKIM
//! material, the hash of a mailbox password and the text of an out-of-office
//! reply (issue #177).
//!
//! The mail daemons read line-oriented files in which a single separator
//! changes the meaning of the line — a colon in a login database, whitespace
//! in a map, a quote or a lone dot in a Sieve script — so every value here has
//! a GRAMMAR and is refused outside it, never escaped (rules/security.md
//! item 4). `check_local_part` is the one reading of an address's local part
//! and is crate-internal: the two public types that carry a local part both
//! call it, so the rule cannot drift between them.

pub mod autoresponder_body;
pub mod autoresponder_body_error;
pub mod autoresponder_subject;
pub mod autoresponder_subject_error;
pub(crate) mod check_local_part;
pub(crate) mod der_reader;
pub mod dkim_private_key;
pub mod dkim_private_key_error;
pub mod dkim_selector;
pub mod dkim_selector_error;
pub(crate) mod is_text_reordering;
pub mod local_submission;
pub mod local_submission_error;
pub mod mail_address;
pub mod mail_address_error;
pub mod mail_alias_targets;
pub mod mail_alias_targets_error;
pub mod mail_domain;
pub mod mail_domain_error;
pub mod mail_local_part;
pub mod mail_local_part_error;
pub mod mail_message_size_mib;
pub mod mail_message_size_mib_error;
pub mod mail_password_hash;
pub mod mail_password_hash_error;
pub mod mail_password_scheme;
pub mod mail_queue_id;
pub mod mail_queue_id_error;
pub mod mail_server_hostname;
pub mod mail_server_hostname_error;
pub mod mailbox_quota_mib;
pub mod mailbox_quota_mib_error;
pub mod send_limit_per_hour;
pub mod send_limit_per_hour_error;
pub mod spam_thresholds;
pub mod spam_thresholds_error;
