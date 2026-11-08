import { defineStore } from 'pinia'
import { computed, ref, shallowRef, type ComputedRef, type Ref, type ShallowRef } from 'vue'
import { useDashboardApi } from '../composables/apis/useDashboardApi'
import { useStreamLifecycle } from '../composables/useStreamLifecycle'
import type { DashboardSection } from '../types/dashboard'
import type {
  LiveActivity,
  LiveHello,
  LiveMetricsPoint,
  LiveStatus,
  LiveTotals,
} from '../types/liveDashboard'
import type { ServiceStatus } from '../types/monitoring'

/**
 * How many readings are kept: the panel's own ring (1800 readings, an hour at two seconds), so the
 * SPA never holds more than the panel would send back.
 */
const SERIES_CAPACITY = 1800

/**
 * Owns the live dashboard: the host's recent readings as one time-ordered series, and the latest
 * services, totals and activity the panel pushed. One stream feeds all of it
 * (`GET /api/v1/dashboard/stream`), opened when the dashboard is on screen and closed when it is
 * left; nothing here polls and no timer runs except the reconnect wait inside the lifecycle.
 *
 * **Back-fill, not replay.** The panel writes no event ids, so continuity after a reconnect is the
 * `since` parameter: the instant of the newest reading held. The panel answers with the readings
 * after it, and they are merged by instant, so a reading that arrives twice (a back-fill that
 * overlaps a live frame) is held once.
 *
 * **Closing forgets.** The series belongs to the identity that opened the stream (an administrator
 * looking at a customer's account sees that customer's frames, not the operator's), so `close`
 * empties everything and the next visit starts from the panel's history.
 */
export const useLiveDashboardStore = defineStore('liveDashboard', () => {
  const api = useDashboardApi()

  /**
   * The connection, kept alive by the shared lifecycle: it closes in a hidden tab, reopens on
   * return with `since` set to {@link newestInstant}, and a stream the panel ends to renew its
   * lifetime reopens without the viewer being shown a reconnect.
   */
  const stream = useStreamLifecycle({
    pauseWhenHidden: true,
    connect: (connection) => {
      return api.openLive(
        {
          onHello: (frame) => {
            hello.value = frame
            connection.delivered()
          },
          onHistory: (frame) => {
            merge(frame.points)
            connection.delivered()
          },
          onMetrics: (frame) => {
            merge([frame])
            connection.delivered()
          },
          onServices: (frame) => {
            services.value = frame.services
          },
          onTotals: (frame) => {
            totals.value = frame
          },
          onActivity: (frame) => {
            activity.value = frame
          },
          onEnd: (reason) => {
            if (reason === 'renew') {
              connection.renew()
            }
          },
        },
        newestInstant,
        connection.signal,
      )
    },
    onRefused: () => {
      forget()
    },
  })

  /**
   * The readings held, oldest first. Shallow on purpose: a reading is replaced, never edited, and
   * deep reactivity over 1800 objects would be tracking nothing anybody reads.
   */
  const points: ShallowRef<LiveMetricsPoint[]> = shallowRef([])

  /** The services the agent watches as last pushed, or `null` before the first frame. */
  const services: Ref<DashboardSection<ServiceStatus[]> | null> = ref(null)

  /** What the server holds and what needs attention as last pushed, or `null` before the first frame. */
  const totals: Ref<LiveTotals | null> = ref(null)

  /** The newest journal entries and notifications as last pushed, or `null` before the first frame. */
  const activity: Ref<LiveActivity | null> = ref(null)

  /** What the panel said when the stream opened, or `null` while no stream has opened. */
  const hello: Ref<LiveHello | null> = ref(null)

  /**
   * The instant of the newest reading held, as the panel wrote it, or `null` with none. What the
   * next connection asks the panel to continue from.
   */
  let newestInstant: string | null = null

  /** Where the connection stands, for the status pill. */
  const status: ComputedRef<LiveStatus> = computed(() => {
    return stream.status.value
  })

  /** The newest reading, or `null` before the first one arrives. */
  const latest: ComputedRef<LiveMetricsPoint | null> = computed(() => {
    return points.value.at(-1) ?? null
  })

  /**
   * Merges readings into the series by instant: a reading already held is not added again, the
   * result is time-ordered, and the oldest are dropped beyond {@link SERIES_CAPACITY}.
   * @param arrivals Readings from a back-fill or a live frame, in any order.
   * @returns Nothing; the series is replaced.
   */
  const merge = (arrivals: LiveMetricsPoint[]): void => {
    const held = points.value
    const known = new Set(
      held.map((point) => {
        return point.t
      }),
    )
    const fresh = arrivals.filter((point) => {
      return !known.has(point.t)
    })
    if (fresh.length === 0) {
      return
    }

    const ordered = [...held, ...fresh].sort((a, b) => {
      return Date.parse(a.t) - Date.parse(b.t)
    })
    points.value = ordered.slice(-SERIES_CAPACITY)
    newestInstant = points.value.at(-1)?.t ?? null
  }

  /**
   * Empties everything held.
   * @returns Nothing.
   */
  const forget = (): void => {
    points.value = []
    services.value = null
    totals.value = null
    activity.value = null
    hello.value = null
    newestInstant = null
  }

  /**
   * Opens the stream for the dashboard that is on screen. Idempotent.
   * @returns Nothing; frames arrive as they are decoded.
   */
  const open = (): void => {
    stream.start()
  }

  /**
   * Closes the stream and forgets what it delivered. Called when the dashboard is left, so no
   * connection and no held figure outlives the screen that shows them.
   * @returns Nothing.
   */
  const close = (): void => {
    stream.stop()
    forget()
  }

  return { points, services, totals, activity, hello, status, latest, open, close }
})
