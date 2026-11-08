import { useApi } from '../useApi'
import type { PanelUpdateApi, PanelUpdateRequested, PanelUpdateState } from '../../types/panelUpdate'

/** The endpoint the panel's release state is read from and an update is requested through. */
const PANEL_UPDATE_PATH = '/api/v1/panel-update'

/**
 * Builds the panel-update API on top of the shared low-level client.
 *
 * The start call sends no body: no version, address or path is the browser's to choose.
 * @returns The {@link PanelUpdateApi} bound to the panel-update endpoint.
 */
export const usePanelUpdateApi = (): PanelUpdateApi => {
  const api = useApi()

  /**
   * Reads the running version and the last check's result.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The panel's release state.
   */
  const get = (signal?: AbortSignal): Promise<PanelUpdateState> => {
    return api.get<PanelUpdateState>(PANEL_UPDATE_PATH, signal)
  }

  /**
   * Requests an update of the panel.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The task that carries the update's progress.
   */
  const start = (signal?: AbortSignal): Promise<PanelUpdateRequested> => {
    return api.post<PanelUpdateRequested>(PANEL_UPDATE_PATH, undefined, signal)
  }

  return { get, start }
}
