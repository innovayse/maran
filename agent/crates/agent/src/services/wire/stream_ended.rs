//! The marker that a pumped stream stopped before it was complete.

/// The channel behind a pump closed without the [`PumpMessage::Complete`]
/// terminator: the pump met a refusal or a stream error, or the client went away and
/// the pump was torn down.
///
/// Carries nothing: why it stopped is the pump's to know (it keeps the refusal), and
/// the blocking side's only decision is that what it has must not be published. Each
/// service maps it onto its own operation's marker.
///
/// [`PumpMessage::Complete`]: crate::services::wire::pump_message::PumpMessage::Complete
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct StreamEnded;
