import { useApi } from '../useApi'
import type { Dashboard, DashboardApi, DashboardSection } from '../../types/dashboard'

/** The endpoint the landing screen's contents are read from. */
const DASHBOARD_PATH = '/api/v1/dashboard'

/** The narrow endpoint the navigation's needs-attention badge is read from. */
const ATTENTION_COUNT_PATH = '/api/v1/dashboard/attention-count'

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

  return { get, attentionCount }
}
