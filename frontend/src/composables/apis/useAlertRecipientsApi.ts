import { useApi } from '../useApi'
import type { AlertRecipients, AlertRecipientsApi, SaveAlertRecipientsRequest } from '../../types/alertSettings'

/** The endpoint the alert recipient list is read from and written to. */
const ALERT_RECIPIENTS_PATH = '/api/v1/notifications/alert-recipients'

/**
 * Builds the alert-recipients API on top of the shared low-level client.
 * @returns The {@link AlertRecipientsApi} bound to the panel's alert-recipients endpoint.
 */
export const useAlertRecipientsApi = (): AlertRecipientsApi => {
  const api = useApi()

  /**
   * Reads the recipient list.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The addresses alert mail goes to.
   */
  const get = (signal?: AbortSignal): Promise<AlertRecipients> => {
    return api.get<AlertRecipients>(ALERT_RECIPIENTS_PATH, signal)
  }

  /**
   * Replaces the recipient list wholesale.
   * @param request The complete list of addresses.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once stored; the server answers 204 with no body.
   */
  const save = (request: SaveAlertRecipientsRequest, signal?: AbortSignal): Promise<void> => {
    return api.put<void>(ALERT_RECIPIENTS_PATH, request, signal)
  }

  return { get, save }
}
