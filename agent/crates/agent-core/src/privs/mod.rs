//! Privilege dropping: the workspace's ONLY home of `unsafe`.
//!
//! The agent runs as root, so every operation on a file inside `/home/<account>/`
//! — creating a document root, writing an ACME challenge, restoring content —
//! goes through [`fork_as_account::fork_as_account`] and runs as the customer.
//! A symlink in the account's home pointing at `/etc/shadow` then reaches a
//! process that cannot read it, instead of one that can. Direct `std::fs` on a
//! customer path as root is forbidden (rules/rust.md "Validation first").
//!
//! This decides *as whom*; something else decides *which* path, and which
//! something depends on what the operation does. Neither half is sufficient
//! alone — containment without a dropped uid is a check an attacker races, and a
//! dropped uid without containment writes wherever the account can reach.
//!
//! **For every write, the other half is the descriptor walk below and not
//! [`crate::validation::fs::path::resolve_in_home`].** This paragraph used to
//! say callers pair the fork with `resolve_in_home`; measured against the
//! workspace, no write does. `ops::files::open_parent_directory` descends from
//! the home one component at a time with `openat`, `O_NOFOLLOW` and an ownership
//! check at every level, and that walk is the containment — it has no window
//! between the check and the use, because a descriptor names an inode.
//! `resolve_in_home` is paired with the fork in exactly two read paths, which
//! must name an entry that already exists; its own documentation says which and
//! why.
//!
//! The `*_in_directory` wrappers are the third leg, and they are why a dropped
//! uid does not have to be trusted to reach the right file. Each one takes a
//! directory the caller already holds OPEN plus a single entry name, so the
//! components above the name cannot be swapped between the check and the use.
//! Together they are enough to walk into a customer's home, create what is
//! missing, write a file and take it away again without ever resolving a path a
//! customer can rewrite: [`open_in_directory`](open_in_directory::open_in_directory)
//! descends, [`make_directory_in_directory`](make_directory_in_directory::make_directory_in_directory)
//! creates a level, [`create_file_in_directory`](create_file_in_directory::create_file_in_directory)
//! brings a new file into existence,
//! [`rename_in_directory`](rename_in_directory::rename_in_directory) puts it in
//! place atomically, and
//! [`remove_file_in_directory`](remove_file_in_directory::remove_file_in_directory)
//! takes an entry away.
//!
//! Changes here require a second reviewer and a threat note
//! (rules/security.md "Sensitive change escalation"). The notes are
//! `docs/superpowers/notes/2026-08-30-privs-threat-note.md`, covering the fork,
//! the id resolution and the ACME challenge write, and
//! `docs/superpowers/notes/2026-08-31-log-tail-openat.md`, covering
//! [`open_in_directory`](open_in_directory::open_in_directory) and the root-side
//! log tail that uses it.
//!
//! `#[allow(unsafe_code)]` is scoped to the modules that hold syscalls, not to
//! the crate: `unsafe` anywhere else in `agent-core` still fails to compile, and
//! every other crate root carries `#![forbid(unsafe_code)]`. Note which modules
//! are NOT in that list — [`directory_entry_name`] and [`priv_error`] hold the
//! judgement and the vocabulary of this module and no syscall at all, so they
//! are compiled under the same ban as the rest of the workspace.

#[allow(unsafe_code)]
pub mod account_ids;
#[allow(unsafe_code)]
pub mod create_file_in_directory;
pub mod directory_entry_name;
#[allow(unsafe_code)]
pub mod fork_as_account;
#[allow(unsafe_code)]
pub mod group_id;
#[allow(unsafe_code)]
pub mod make_directory_in_directory;
#[allow(unsafe_code)]
pub mod open_in_directory;
pub mod priv_error;
#[allow(unsafe_code)]
pub mod remove_file_in_directory;
#[allow(unsafe_code)]
pub mod rename_in_directory;
