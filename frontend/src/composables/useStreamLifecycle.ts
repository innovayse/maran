import { readonly, ref, type Ref } from 'vue'
import type { LiveStatus } from '../types/liveDashboard'
import { ApiError } from './useApi'

/** How long a stream waits before its first reconnect after the connection ended. */
const RECONNECT_FIRST_DELAY_MS = 1000

/**
 * The longest wait between two reconnects. The wait doubles after each attempt that delivered
 * nothing, so a panel that is down is asked twice a minute rather than hammered, and a restarted
 * one is found within half a minute.
 */
const RECONNECT_MAX_DELAY_MS = 30000

/**
 * HTTP statuses that mean the panel refused the stream rather than lost it: signed out, not
 * allowed, or the feature absent. Retrying cannot change any of them, so the stream stops.
 */
const DEFAULT_REFUSAL_STATUSES: readonly number[] = [401, 403, 404]

/** What one connection attempt is handed: its abort signal and the two things it can tell the lifecycle. */
export interface StreamConnection {
  /** Aborted when the lifecycle closes, pauses or replaces this connection. */
  signal: AbortSignal
  /**
   * Reports that real data arrived. The lifecycle then shows `live` and starts the reconnect wait
   * over, so a connection that opens and delivers nothing keeps backing off.
   */
  delivered: () => void
  /**
   * Reports that the panel is ending this stream on purpose to be reopened (its lifetime cap).
   * The next connection opens at once and the viewer is never shown `reconnecting`.
   */
  renew: () => void
}

/** What a caller gives {@link useStreamLifecycle}. */
export interface StreamLifecycleOptions {
  /**
   * Opens one connection and resolves when it ended (rejects when it could not be opened). Called
   * again for every reconnect, so anything that must survive one (the newest instant held, for a
   * back-fill) lives with the caller.
   */
  connect: (connection: StreamConnection) => Promise<void>
  /** Whether a hidden tab closes the connection and a visible one reopens it. Off keeps it open. */
  pauseWhenHidden: boolean
  /** Called when the panel refused the stream and no retry will be made; the caller drops what it held. */
  onRefused?: () => void
  /** Statuses that mean a refusal. Defaults to 401, 403 and 404. */
  refusalStatuses?: readonly number[]
}

/** What {@link useStreamLifecycle} returns. */
export interface StreamLifecycle {
  /** Where the connection stands, for the viewer. */
  status: Readonly<Ref<LiveStatus>>
  /** Opens the stream; does nothing when it is already open, waiting or paused. */
  start: () => void
  /** Closes the stream, cancels any pending reconnect and forgets the backoff. */
  stop: () => void
  /** Closes and opens again at once (for example after the interface language changed). */
  restart: () => void
}

/**
 * Keeps one Server-Sent Events connection alive: opens it, reopens it after it breaks with a
 * doubling wait, stops when the panel refuses it, optionally closes it while the tab is hidden and
 * opens it again on return, and reopens it without a visible gap when the panel ends it to renew.
 *
 * The module is a composable and not a store so the two streams that need it (the dashboard's and
 * the running-tasks tray) share the behaviour, not the state: each call owns its own timers,
 * controller and status. It may be called outside a component (a store's setup), so it registers
 * nothing that needs a component scope; the visibility listener is added by `start` and removed by
 * `stop`.
 * @param options What to connect and how to behave; see {@link StreamLifecycleOptions}.
 * @returns The status and the three controls.
 */
export const useStreamLifecycle = (options: StreamLifecycleOptions): StreamLifecycle => {
  const refusals = options.refusalStatuses ?? DEFAULT_REFUSAL_STATUSES

  /** Where the connection stands. */
  const status: Ref<LiveStatus> = ref('stopped')

  /** The open connection's controller, or `null` when none is open. */
  let current: AbortController | null = null

  /** Whether `start` ran and `stop` has not: the stream is meant to be running (or paused). */
  let wanted = false

  /** The reconnect waiting to happen, or `null`. */
  let timer: ReturnType<typeof setTimeout> | null = null

  /** How long the next reconnect waits. */
  let delay = RECONNECT_FIRST_DELAY_MS

  /** Whether the connection that just ended has been marked as a deliberate renewal. */
  let renewing = false

  /**
   * Whether the tab is hidden right now and this stream is told to care.
   * @returns True when the connection should stay closed.
   */
  const shouldPause = (): boolean => {
    return options.pauseWhenHidden && typeof document !== 'undefined' && document.visibilityState === 'hidden'
  }

  /**
   * Cancels a pending reconnect.
   * @returns Nothing.
   */
  const clearTimer = (): void => {
    if (timer !== null) {
      clearTimeout(timer)
      timer = null
    }
  }

  /**
   * Aborts the open connection, if any, so its ending is recognised as not ours to handle.
   * @returns Nothing.
   */
  const closeConnection = (): void => {
    const controller = current
    current = null
    controller?.abort()
  }

  /**
   * Decides what follows the end of a connection: a refusal stops, a renewal reopens at once, and
   * anything else waits and reopens with a longer wait next time.
   * @param controller The connection that ended.
   * @param cause Why: `null` for a clean end, otherwise what was thrown.
   * @returns Nothing.
   */
  const ended = (controller: AbortController, cause: unknown): void => {
    // Closed, paused or replaced on purpose: whoever did it owns what happens next.
    if (current !== controller) {
      return
    }
    current = null

    if (cause instanceof ApiError && refusals.includes(cause.status)) {
      // Fully stopped, not merely idle: a later `start` (a new identity) must be able to open again.
      stop()
      options.onRefused?.()
      return
    }

    if (renewing) {
      renewing = false
      open()
      return
    }

    status.value = 'reconnecting'
    timer = setTimeout(() => {
      timer = null
      open()
    }, delay)
    delay = Math.min(delay * 2, RECONNECT_MAX_DELAY_MS)
  }

  /**
   * Opens one connection now.
   * @returns Nothing.
   */
  const open = (): void => {
    const controller = new AbortController()
    current = controller
    renewing = false
    // A renewal reopens inside the same `live` stretch; only a first connection reads `connecting`.
    if (status.value !== 'live' && status.value !== 'reconnecting') {
      status.value = 'connecting'
    }

    options
      .connect({
        signal: controller.signal,
        delivered: () => {
          if (current === controller) {
            status.value = 'live'
            delay = RECONNECT_FIRST_DELAY_MS
          }
        },
        renew: () => {
          if (current === controller) {
            renewing = true
          }
        },
      })
      .then(
        () => {
          ended(controller, null)
        },
        (cause: unknown) => {
          ended(controller, cause)
        },
      )
  }

  /**
   * Follows the tab: closes the connection when it is hidden and reopens it when it is visible again.
   * @returns Nothing.
   */
  const onVisibilityChange = (): void => {
    if (!wanted) {
      return
    }

    if (shouldPause()) {
      clearTimer()
      closeConnection()
      status.value = 'paused'
      return
    }

    if (status.value === 'paused') {
      delay = RECONNECT_FIRST_DELAY_MS
      open()
    }
  }

  /**
   * Opens the stream; does nothing when it is already open, waiting or paused.
   * @returns Nothing.
   */
  const start = (): void => {
    if (wanted) {
      return
    }
    wanted = true
    if (options.pauseWhenHidden && typeof document !== 'undefined') {
      document.addEventListener('visibilitychange', onVisibilityChange)
    }

    if (shouldPause()) {
      status.value = 'paused'
      return
    }
    open()
  }

  /**
   * Closes the stream, cancels a pending reconnect and forgets the backoff.
   * @returns Nothing.
   */
  const stop = (): void => {
    wanted = false
    if (typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', onVisibilityChange)
    }
    clearTimer()
    closeConnection()
    renewing = false
    delay = RECONNECT_FIRST_DELAY_MS
    status.value = 'stopped'
  }

  /**
   * Closes the stream and opens it again at once.
   * @returns Nothing.
   */
  const restart = (): void => {
    stop()
    start()
  }

  return { status: readonly(status), start, stop, restart }
}
