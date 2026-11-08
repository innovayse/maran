//! The shape of the closure that feeds a forked child its input.

use std::io;

use super::child_input_writer::ChildInputWriter;

/// A closure that writes a child's input through the parent's bounded handle.
///
/// Passed as `Option<&mut ChildInputFeed<'_>>` to
/// [`fork_reporting_as_account`](super::fork_reporting_as_account::fork_reporting_as_account). It
/// runs on the calling thread; finishing without [`ChildInputWriter::complete`], or with an error,
/// is an abort. The object lifetime is the closure's own borrow, so a feed may capture locals.
pub type ChildInputFeed<'feed> = dyn FnMut(&mut ChildInputWriter<'_>) -> io::Result<()> + 'feed;
