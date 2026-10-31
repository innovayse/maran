import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useSitesApi } from '../composables/apis/useSitesApi'
import type {
  SiteLogEndReason,
  SiteLogLine,
  SiteLogSource,
  SiteLogStreamStatus,
} from '../types/siteLog'

/**
 * How many historical lines a log tail asks for. A UI concern — how much scrollback a person
 * gets on opening the tab — not a domain value, so it lives here (rules/vue.md).
 */
const LOG_HISTORY_LINES = 200

/**
 * The most lines the store keeps in memory for one stream. A log that is appended to for hours
 * would otherwise grow the array without bound and take the tab down with it; the oldest lines
 * are dropped as new ones arrive, which is a scrollback limit and is why the view shows how many
 * lines it holds rather than implying it holds all of them.
 */
const MAX_LOG_LINES = 2000

/**
 * Owns the live tail of one site's log: the lines held, whether the stream is open, and why it
 * ended. The log tab reads state from here; the detail page stops the stream when it is left.
 */
export const useSiteLogsStore = defineStore('siteLogs', () => {
  const api = useSitesApi()

  /** The lines of the log currently being tailed, oldest first, capped at {@link MAX_LOG_LINES}. */
  const logLines: Ref<SiteLogLine[]> = ref([])

  /** Whether a log stream is open, has ended, or was never started. */
  const logStatus: Ref<SiteLogStreamStatus> = ref('idle')

  /**
   * Why the last log stream ended, or `null` while one is open or none has run.
   *
   * The whole point of keeping it: a pane that stopped updating looks the same whether the log
   * simply had nothing more to say, was dropped, was truncated, or failed. The view renders this
   * reason, so a truncation is never shown as a normal end.
   */
  const logEndReason: Ref<SiteLogEndReason | null> = ref(null)

  /** The backend's own explanation of a failed log stream, or `null` when it sent none. */
  const logEndMessage: Ref<string | null> = ref(null)

  /**
   * Whether this view's own scrollback cap has already dropped lines the stream delivered.
   *
   * A second, quieter truncation than {@link logEndReason}'s: the stream is intact, but what is
   * held is not all of it. The pane says so, because an operator scrolling to the top of a log
   * must not read the oldest line it holds as the oldest line there was.
   */
  const logScrollbackTruncated: Ref<boolean> = ref(false)

  /**
   * Aborts the open log stream, or `null` when none is open. Held so the store can stop a stream
   * on unmount, on a source switch, and on navigation: a stream nobody aborts keeps its
   * connection open for the life of the tab.
   */
  const logAbort: Ref<AbortController | null> = ref(null)

  /**
   * Identifies the stream whose events are currently welcome. A stream that has been replaced
   * can still deliver one last event before its abort lands, and appending that to the new
   * stream's lines would interleave two logs; comparing this token drops it instead.
   */
  const logStreamToken: Ref<number> = ref(0)

  /**
   * Stops the open log stream, if any, and releases its connection.
   *
   * Safe to call when nothing is open, and safe to call twice — the log tab calls it on unmount,
   * where neither condition can be assumed.
   * @returns Nothing.
   */
  const stopLogTail = (): void => {
    const controller = logAbort.value
    if (controller === null) {
      return
    }

    logAbort.value = null
    controller.abort()
  }

  /**
   * Opens a live tail of one of a site's logs, replacing any stream already open.
   *
   * Resolves when the stream ends, so a caller may await it; the ending itself is left in
   * {@link logStatus}, {@link logEndReason} and {@link logEndMessage} rather than returned,
   * because the pane has to keep showing it after the promise is long settled.
   * @param siteId The site whose log is read.
   * @param source Which of the site's two logs to tail.
   * @returns Resolves once the stream has ended.
   */
  const startLogTail = async (siteId: string, source: SiteLogSource): Promise<void> => {
    // A second tab click must not leave the first stream pulling in the background.
    stopLogTail()

    const controller = new AbortController()
    logAbort.value = controller
    logStreamToken.value += 1
    const token = logStreamToken.value

    logLines.value = []
    logScrollbackTruncated.value = false
    logEndReason.value = null
    logEndMessage.value = null
    logStatus.value = 'streaming'

    await api.tailLog(
      { siteId, source, historyLines: LOG_HISTORY_LINES },
      {
        /**
         * Appends one line, dropping the oldest once the scrollback cap is reached.
         * @param line The line the panel sent.
         * @returns Nothing.
         */
        onLine: (line: SiteLogLine): void => {
          if (token !== logStreamToken.value) {
            return
          }

          const next = [...logLines.value, line]
          if (next.length > MAX_LOG_LINES) {
            // Latched rather than derived from the length: once a line has been dropped the pane
            // is permanently incomplete, and the array stays at the cap so its length alone
            // could not say whether the cap was ever exceeded.
            logScrollbackTruncated.value = true
            logLines.value = next.slice(next.length - MAX_LOG_LINES)
            return
          }
          logLines.value = next
        },

        /**
         * Records why the stream ended, so the view can say so rather than simply stopping.
         * @param reason Why the stream ended.
         * @param message The backend's explanation, or null.
         * @returns Nothing.
         */
        onEnd: (reason: SiteLogEndReason, message: string | null): void => {
          if (token !== logStreamToken.value) {
            return
          }

          logEndReason.value = reason
          logEndMessage.value = message
          logStatus.value = 'ended'
          logAbort.value = null
        },
      },
      controller.signal,
    )
  }

  return {
    logLines,
    logStatus,
    logEndReason,
    logEndMessage,
    logScrollbackTruncated,
    startLogTail,
    stopLogTail,
  }
})
