//! Draining one of a child's output streams without retaining past a cap.

use std::io::{ErrorKind, Read};
use std::sync::mpsc::Sender;

use crate::utils::child_event::ChildEvent;
use crate::utils::output_stream::OutputStream;

/// Bytes read from the pipe per `read` call. Never retained beyond the cap.
const CHUNK: usize = 8 * 1024;

/// Starts a thread that drains `pipe` to end-of-file and reports once on `events`.
///
/// The thread keeps what it reads only while the total stays within `cap`. The
/// first read that would carry the total past `cap` ends it with
/// [`ChildEvent::Overflow`] and nothing is reported as captured, so a caller can
/// never be handed a shortened stream. The daemon therefore holds at most `cap`
/// bytes per stream plus one chunk, whatever the child prints.
///
/// One thread per captured stream and no more: this is the cost
/// `Command::output()` did not have, and it is the smallest that lets the two
/// streams drain concurrently (reading them one after the other deadlocks a child
/// that fills the second pipe while the first is still open). The thread is NOT
/// joined by anybody. It ends when the pipe reaches end-of-file, which happens
/// when the child and every descendant holding the write end are gone, or when
/// the waiting side stops listening. A descendant that has left the killed
/// process group and keeps the pipe open keeps this thread alive for as long as
/// it does; it retains nothing past the cap, but it is a task.
///
/// # Errors
///
/// Returns the `io::Error` of failing to start the thread.
pub(crate) fn start_reader<R>(
    mut pipe: R,
    stream: OutputStream,
    cap: u64,
    events: Sender<ChildEvent>,
) -> std::io::Result<()>
where
    R: Read + Send + 'static,
{
    std::thread::Builder::new()
        .name("maran-spawn-reader".to_owned())
        .spawn(move || {
            let event = drain(&mut pipe, stream, cap);
            // The waiting side may have given up and dropped its receiver; there
            // is nobody left to tell.
            let _ = events.send(event);
        })
        .map(drop)
}

/// Reads `pipe` to end-of-file, keeping at most `cap` bytes.
///
/// A read that is interrupted is retried; any other read error ends the drain
/// as [`ChildEvent::ReadFailed`]. Exactly `cap` bytes is accepted.
fn drain<R: Read>(pipe: &mut R, stream: OutputStream, cap: u64) -> ChildEvent {
    let mut kept: Vec<u8> = Vec::new();
    let mut chunk = [0_u8; CHUNK];

    loop {
        match pipe.read(&mut chunk) {
            Ok(0) => {
                return ChildEvent::Output {
                    stream,
                    bytes: kept,
                };
            }
            Ok(read) => {
                let total = u64::try_from(kept.len())
                    .unwrap_or(u64::MAX)
                    .saturating_add(u64::try_from(read).unwrap_or(u64::MAX));
                if total > cap {
                    return ChildEvent::Overflow(stream);
                }
                kept.extend_from_slice(chunk.get(..read).unwrap_or_default());
            }
            Err(error) if error.kind() == ErrorKind::Interrupted => {}
            Err(_) => return ChildEvent::ReadFailed(stream),
        }
    }
}
