import { useApi } from '../useApi'
import type { AlertThresholds, AlertThresholdsApi, SaveAlertThresholdsRequest } from '../../types/alertSettings'

/** The endpoint the alert thresholds are read from and written to. */
const ALERT_THRESHOLDS_PATH = '/api/v1/monitoring/alert-thresholds'

/**
 * Builds the alert-thresholds API on top of the shared low-level client.
 * @returns The {@link AlertThresholdsApi} bound to the panel's alert-thresholds endpoint.
 */
export const useAlertThresholdsApi = (): AlertThresholdsApi => {
  const api = useApi()

  /**
   * Reads the thresholds in force and the bounds a threshold may take.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The thresholds with their bounds.
   */
  const get = (signal?: AbortSignal): Promise<AlertThresholds> => {
    return api.get<AlertThresholds>(ALERT_THRESHOLDS_PATH, signal)
  }

  /**
   * Replaces all three thresholds.
   * @param request The complete thresholds; `null` stops watching the processor or the memory.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once stored; the server answers 204 with no body.
   */
  const save = (request: SaveAlertThresholdsRequest, signal?: AbortSignal): Promise<void> => {
    return api.put<void>(ALERT_THRESHOLDS_PATH, request, signal)
  }

  return { get, save }
}
