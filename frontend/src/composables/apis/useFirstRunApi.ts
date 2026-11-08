import { useApi } from '../useApi'
import type { FirstRunApi, FirstRunChecklist } from '../../types/firstRun'

/** The endpoint the first-run checklist is read from. */
const FIRST_RUN_PATH = '/api/v1/dashboard/first-run'

/**
 * Builds the first-run checklist API on top of the shared low-level client. One read and no
 * parameters: the panel works out what is done.
 * @returns The {@link FirstRunApi} bound to the checklist endpoint.
 */
export const useFirstRunApi = (): FirstRunApi => {
  const api = useApi()

  /**
   * Reads the checklist.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The steps and whether all are done.
   */
  const get = (signal?: AbortSignal): Promise<FirstRunChecklist> => {
    return api.get<FirstRunChecklist>(FIRST_RUN_PATH, signal)
  }

  return { get }
}
