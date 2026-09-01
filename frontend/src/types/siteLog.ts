/**
 * Which of a site's two logs is being tailed, mirroring the agent's `SiteLogKind` and the
 * backend's `SiteLogSource`.
 */
export type SiteLogSource = 'access' | 'error'

/**
 * How a tailed log stream ended.
 *
 * The distinction is the whole point of the type, and it is carried all the way to the screen.
 * A pane that stops updating looks identical whether the agent closed the stream with nothing
 * left to send, dropped it because the panel stopped reading, closed it after its idle timeout,
 * failed the operation outright, truncated it, or was cancelled by the operator. Collapsing
 * those into "the stream finished" is a silent truncation dressed up as a normal end — an
 * operator would go on watching a pane that will never update again. Every ending therefore
 * arrives named, and the store keeps the name.
 */
export type SiteLogEndReason =
  /** The agent closed the stream normally, with no further lines to send. */
  | 'completed'
  /** The agent dropped the stream because the reader fell behind. Retryable by reopening it. */
  | 'dropped'
  /** The agent closed the stream after its maximum idle time. Benign: nothing more was logged. */
  | 'idle'
  /** The operation itself failed; the accompanying message says why, in the backend's words. */
  | 'failed'
  /** The stream was cut short and lines are missing. Never to be shown as a normal end. */
  | 'truncated'
  /** The panel stopped watching — a closed view, a navigation, an unmounted component. */
  | 'cancelled'

/** One line from a tailed log, as the panel emits it on the stream. */
export interface SiteLogLine {
  /** The raw log line, without its trailing newline. Customer-supplied text: never trusted markup. */
  line: string
  /** True for lines replayed from the existing tail, false for lines appended live. */
  historical: boolean
}

/**
 * How the stream a site's log tab is watching currently stands.
 *
 * `idle` here means "no stream has been opened", which is NOT the agent's
 * {@link SiteLogEndReason} `idle` — that one is an ending. They are separate types for that
 * reason: a state and a reason must not be comparable by accident.
 */
export type SiteLogStreamStatus = 'idle' | 'streaming' | 'ended'

/** What a caller asks for when opening a log stream. */
export interface TailSiteLogOptions {
  /** The site whose log is read. */
  siteId: string
  /** Which of the site's two logs to tail. */
  source: SiteLogSource
  /** How many historical lines to replay before switching to live tailing. */
  historyLines: number
}

/**
 * The callbacks a log stream drives. Exactly one terminal call is made per stream — `onEnd`,
 * always, whatever the ending — so a caller has one place to stop a spinner and one place to
 * decide what the operator is told.
 */
export interface SiteLogStreamHandlers {
  /**
   * Called once per log line, in the order the panel sent them.
   * @param line The line and whether it came from the historical tail.
   * @returns Nothing.
   */
  onLine: (line: SiteLogLine) => void

  /**
   * Called exactly once, when the stream ends, whatever the ending.
   * @param reason Why the stream ended.
   * @param message The backend's already-localized explanation, or `null` when it sent none.
   * @returns Nothing.
   */
  onEnd: (reason: SiteLogEndReason, message: string | null) => void
}
