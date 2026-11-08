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
//! takes an entry away. Two more serve the account-deletion sweep:
//! [`stat_in_directory`](stat_in_directory::stat_in_directory) reads an entry's
//! owner and kind without following a link, and
//! [`remove_directory_in_directory`](remove_directory_in_directory::remove_directory_in_directory)
//! takes away a directory that is already empty, and
//! [`list_directory_names`](list_directory_names::list_directory_names) reads the names in a
//! directory held open, bounded. Their threat note is
//! `docs/superpowers/notes/2026-10-29-account-deletion-leftovers-threat-note.md`.
//!
//! **Data comes back from the dropped child through exactly two doors, each bounded and each
//! made by the parent before the fork.** [`fork_as_account::fork_as_account`] answers with an
//! exit status and nothing else. The operations that browse a customer's tree need more: a
//! listing or a search has a RESULT, and a download has a FILE. They go through
//! [`fork_as_account_for_bytes::fork_as_account_for_bytes`] (a byte string over a pipe, at most
//! the cap the caller states, discarded unless the child exits successfully) and
//! [`fork_as_account_for_descriptor::fork_as_account_for_descriptor`] (one read-only descriptor
//! over a socket pair, received with `SCM_RIGHTS`). The alternative — reading the tree as root
//! with an ownership check at every level — makes one missing check a disclosure of any file on
//! the host, and the rule that customer file operations run under the account's uid exists to
//! avoid exactly that. All three entry points share one fork, one drop, one verification and one
//! descriptor sweep; a channel is a descriptor the sweep spares, one, and nothing else. The
//! threat note is `docs/superpowers/notes/2026-11-07-file-manager-browse-threat-note.md`; its
//! second review is OUTSTANDING (rules/security.md "Sensitive change escalation").
//!
//! **A structured answer and an upload go through one more door.**
//! [`fork_reporting_as_account::fork_reporting_as_account`] gives the child exactly one descriptor,
//! a socket pair end made by the parent before the fork, and puts both directions on it: the
//! parent can feed the child a framed upload ([`child_input_writer::ChildInputWriter`]), and the
//! child answers with a fixed 17-byte report of two integers or one refusal number
//! ([`child_report::ChildReport`], [`child_refusal::ChildRefusal`]). No length, string or path
//! crosses from the child; the parent decodes integers only and reads anything else as a failure.
//! Same fork, drop, verification and sweep as the others.
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
//! are NOT in that list — [`directory_entry_name`], [`directory_names`], [`entry_kind`], [`entry_stat`],
//! [`priv_error`] and [`result_sink`] hold the judgement and the vocabulary of this module and
//! no syscall at all, and [`fork_as_account_for_bytes`] and [`fork_as_account_for_descriptor`]
//! hold only the parent's side of the two channels, which `std` already wraps safely (a pipe,
//! a socket pair, a read). All of them are compiled under the same ban as the rest of the
//! workspace, so a stray `unsafe` in the parent's half of a channel fails to compile instead of
//! being waved through by the exception its neighbour needs.

#[allow(unsafe_code)]
pub mod account_ids;
pub mod child_context;
mod child_exit_status;
pub mod child_input_feed;
pub mod child_input_writer;
pub mod child_refusal;
pub mod child_report;
pub mod child_tally;
#[allow(unsafe_code)]
mod close_inherited_descriptors;
#[allow(unsafe_code)]
pub mod create_file_in_directory;
mod decode_report;
mod decoded_report;
#[allow(unsafe_code)]
mod descriptor_is_read_only;
pub mod directory_entry_name;
pub mod directory_names;
#[allow(unsafe_code)]
mod drop_to_account;
mod encode_report;
pub mod entry_kind;
pub mod entry_stat;
#[allow(unsafe_code)]
mod fork_and_run;
#[allow(unsafe_code)]
pub mod fork_as_account;
pub mod fork_as_account_for_bytes;
pub mod fork_as_account_for_descriptor;
pub mod fork_reporting_as_account;
#[allow(unsafe_code)]
pub mod group_id;
pub mod input_frame;
#[allow(unsafe_code)]
mod last_errno;
#[allow(unsafe_code)]
pub mod list_directory_names;
#[allow(unsafe_code)]
pub mod make_directory_in_directory;
#[allow(unsafe_code)]
pub mod open_in_directory;
#[allow(unsafe_code)]
mod poll_until_readable;
pub mod priv_error;
#[allow(unsafe_code)]
mod receive_descriptor;
#[allow(unsafe_code)]
pub mod remove_directory_in_directory;
#[allow(unsafe_code)]
pub mod remove_file_in_directory;
#[allow(unsafe_code)]
pub mod rename_in_directory;
#[allow(unsafe_code)]
pub mod rename_no_replace_between_directories;
pub mod result_sink;
#[allow(unsafe_code)]
mod send_descriptor;
#[allow(unsafe_code)]
pub mod stat_in_directory;
#[allow(unsafe_code)]
mod wait_for_child;
