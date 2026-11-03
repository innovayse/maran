import { useApi } from '../useApi'
import type { CronOptions, CronOptionsApi } from '../../types/cronOptions'

/** The endpoint the scheduled-tasks screens' choice lists are read from. */
const CRON_OPTIONS_PATH = '/api/v1/cron-options'

/**
 * Builds the cron options API on top of the shared low-level client.
 * @returns The {@link CronOptionsApi} bound to the panel's cron options endpoint.
 */
export const useCronOptionsApi = (): CronOptionsApi => {
  const api = useApi()

  /**
   * Reads every closed set the scheduled-tasks screens select from.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The lists, named in the language the request carries.
   */
  const get = (signal?: AbortSignal): Promise<CronOptions> => {
    return api.get<CronOptions>(CRON_OPTIONS_PATH, signal)
  }

  return { get }
}
