import { useApi } from '../useApi'
import type { EnableMailServerRequest, MailServerApi, MailServerStatus } from '../../types/mailServer'

/** The endpoint the mail server's status is read from. */
const MAIL_SERVER_PATH = '/api/v1/mail-server'

/** The endpoint that turns the mail server on. */
const MAIL_SERVER_ENABLE_PATH = '/api/v1/mail-server/enable'

/**
 * Builds the mail server API on top of the shared low-level client.
 * @returns The {@link MailServerApi} bound to the panel's mail server endpoints.
 */
export const useMailServerApi = (): MailServerApi => {
  const api = useApi()

  /**
   * Reads the mail server's status.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The status.
   */
  const status = (signal?: AbortSignal): Promise<MailServerStatus> => {
    return api.get<MailServerStatus>(MAIL_SERVER_PATH, signal)
  }

  /**
   * Turns the mail server on.
   * @param request The host name and the starting values.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The status after the daemons were configured.
   */
  const enable = (request: EnableMailServerRequest, signal?: AbortSignal): Promise<MailServerStatus> => {
    return api.post<MailServerStatus>(MAIL_SERVER_ENABLE_PATH, request, signal)
  }

  return { status, enable }
}
