import { useApi } from '../useApi'
import type { DashboardLayout, DashboardLayoutApi, SaveDashboardLayoutRequest } from '../../types/dashboardLayout'

/** The endpoint the signed-in person's own dashboard layout is read from and written to. */
const DASHBOARD_LAYOUT_PATH = '/api/v1/me/dashboard-layout'

/**
 * Builds the dashboard-layout API on top of the shared low-level client. The endpoint is scoped to
 * the caller's own token, so no call here names a user.
 * @returns The {@link DashboardLayoutApi} bound to the layout endpoint.
 */
export const useDashboardLayoutApi = (): DashboardLayoutApi => {
  const api = useApi()

  /**
   * Reads the effective layout.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The widgets and shortcuts in the order they are drawn, with the person's choices applied.
   */
  const get = (signal?: AbortSignal): Promise<DashboardLayout> => {
    return api.get<DashboardLayout>(DASHBOARD_LAYOUT_PATH, signal)
  }

  /**
   * Replaces the stored layout wholesale.
   * @param request The complete layout.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once stored; the server answers 204 with no body.
   */
  const save = (request: SaveDashboardLayoutRequest, signal?: AbortSignal): Promise<void> => {
    return api.put<void>(DASHBOARD_LAYOUT_PATH, request, signal)
  }

  return { get, save }
}
