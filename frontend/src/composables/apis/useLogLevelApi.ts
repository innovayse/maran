import { useApi } from '../useApi'
import type { ChangeLogLevelRequest, LogLevel, LogLevelApi } from '../../types/logLevel'

/** The endpoint the panel's log level is read from and written to. */
const LOG_LEVEL_PATH = '/api/v1/log-level'

/**
 * Builds the log-level API on top of the shared low-level client (issue #131).
 *
 * Two calls on one path, because the panel has exactly one level: a GET that reads it with the
 * choices, and a PUT that applies one.
 * @returns The {@link LogLevelApi} bound to the panel's log-level endpoint.
 */
export const useLogLevelApi = (): LogLevelApi => {
  const api = useApi()

  /**
   * Reads the running level and the levels on offer.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The level and its choices.
   */
  const get = (signal?: AbortSignal): Promise<LogLevel> => {
    return api.get<LogLevel>(LOG_LEVEL_PATH, signal)
  }

  /**
   * Applies a level.
   * @param request The level to apply.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns True once it has been applied and stored.
   */
  const change = (request: ChangeLogLevelRequest, signal?: AbortSignal): Promise<boolean> => {
    return api.put<boolean>(LOG_LEVEL_PATH, request, signal)
  }

  return { get, change }
}
