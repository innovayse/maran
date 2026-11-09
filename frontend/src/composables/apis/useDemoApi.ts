import { useApi } from '../useApi'
import type { DemoApi, DemoStatus } from '../../types/demo'

/** The anonymous endpoint the demo distribution answers with its status. */
const STATUS_PATH = '/api/v1/demo/status'

/**
 * Builds the demo API on top of the shared low-level client.
 * @returns The {@link DemoApi} bound to the demo status endpoint.
 */
export const useDemoApi = (): DemoApi => {
  const api = useApi()

  /**
   * Reads the demo's status: the reset schedule, the published logins and the refusal reasons.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The status.
   */
  const status = (signal?: AbortSignal): Promise<DemoStatus> => {
    return api.get<DemoStatus>(STATUS_PATH, signal)
  }

  return { status }
}
