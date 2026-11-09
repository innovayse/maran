//! The child's side of an upload: the framed channel seen as an [`UploadSource`].

use maran_agent_core::privs::child_context::ChildContext;
use maran_agent_core::privs::input_frame::InputFrame;

use crate::files::model::upload_aborted::UploadAborted;
use crate::files::model::upload_source::UploadSource;

/// Adapts the forked child's channel to the source the writer reads.
///
/// A terminator is the end of a complete upload; a channel that ends, breaks or carries an
/// oversized frame without one is [`UploadAborted`], which the writer answers by removing its
/// temporary file.
pub(crate) struct ChildUpload<'context, 'channel> {
    /// The child's end of the channel.
    context: &'context mut ChildContext<'channel>,
}

impl<'context, 'channel> ChildUpload<'context, 'channel> {
    /// Wraps the channel.
    pub(crate) fn new(context: &'context mut ChildContext<'channel>) -> Self {
        Self { context }
    }
}

impl UploadSource for ChildUpload<'_, '_> {
    fn next_chunk(&mut self) -> Result<Option<&[u8]>, UploadAborted> {
        match self.context.read_frame() {
            InputFrame::Data(block) => Ok(Some(block)),
            InputFrame::Complete => Ok(None),
            InputFrame::Aborted => Err(UploadAborted),
        }
    }
}
