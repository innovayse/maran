import { useApi } from '../useApi'
import type { Dashboard, DashboardApi, DashboardSection } from '../../types/dashboard'
import type {
  LiveActivity,
  LiveDashboardHandlers,
  LiveHello,
  LiveHistory,
  LiveServices,
  LiveMetricsPoint,
  LiveTotals,
} from '../../types/liveDashboard'

/** The endpoint the landing screen's contents are read from. */
const DASHBOARD_PATH = '/api/v1/dashboard'

/** The narrow endpoint the navigation's needs-attention badge is read from. */
const ATTENTION_COUNT_PATH = '/api/v1/dashboard/attention-count'

/** The live stream: host readings, services, totals and activity as Server-Sent Events. */
const LIVE_STREAM_PATH = '/api/v1/dashboard/stream'

/**
 * Builds the dashboard API on top of the shared low-level client.
 *
 * One read and no parameters: the panel decides what this caller may be shown, so there is nothing
 * for the SPA to ask for and nothing it could ask for that would change the answer.
 * @returns The {@link DashboardApi} bound to the panel's dashboard endpoint.
 */
export const useDashboardApi = (): DashboardApi => {
  const api = useApi()

  /**
   * Reads the landing screen's contents for the signed-in caller.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns What this caller is allowed to be shown.
   */
  const get = (signal?: AbortSignal): Promise<Dashboard> => {
    return api.get<Dashboard>(DASHBOARD_PATH, signal)
  }

  /**
   * Reads the needs-attention figure alone, for the navigation badge.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The figure as the panel answered it: loaded with the count, or unavailable.
   */
  const attentionCount = (signal?: AbortSignal): Promise<DashboardSection<number>> => {
    return api.get<DashboardSection<number>>(ATTENTION_COUNT_PATH, signal)
  }

  /**
   * Opens the live stream and routes each decoded frame to its handler by event name.
   *
   * Goes through the shared `stream` helper, which carries the bearer token and the CSRF header
   * (`EventSource` sends neither). A frame whose name is not listed here is skipped: the panel's
   * vocabulary grows additively, and an older SPA must keep working against a newer panel.
   * @param handlers Where each kind of frame goes.
   * @param since Instant of the newest reading already held, or `null` on a first connection.
   * @param signal Abort signal that closes the stream and releases its connection.
   * @returns Resolves when the stream ended or was aborted; rejects when it could not be opened.
   */
  const openLive = async (
    handlers: LiveDashboardHandlers,
    since: string | null,
    signal: AbortSignal,
  ): Promise<void> => {
    const query = since === null ? '' : `?since=${encodeURIComponent(since)}`
    await api.stream(
      `${LIVE_STREAM_PATH}${query}`,
      (event) => {
        switch (event.name) {
          case 'hello':
            handlers.onHello(JSON.parse(event.data) as LiveHello)
            break
          case 'history':
            handlers.onHistory(JSON.parse(event.data) as LiveHistory)
            break
          case 'metrics':
            handlers.onMetrics(JSON.parse(event.data) as LiveMetricsPoint)
            break
          case 'services':
            handlers.onServices(JSON.parse(event.data) as LiveServices)
            break
          case 'totals':
            handlers.onTotals(JSON.parse(event.data) as LiveTotals)
            break
          case 'activity':
            handlers.onActivity(JSON.parse(event.data) as LiveActivity)
            break
          case 'end': {
            const payload = JSON.parse(event.data) as { reason?: unknown }
            handlers.onEnd(payload.reason === 'renew' ? 'renew' : 'shutdown')
            break
          }
          default:
            // An unknown frame is a newer panel's; ignoring it keeps this SPA working against it.
            break
        }
      },
      signal,
    )
  }

  return { get, attentionCount, openLive }
}
