import { useApi } from '../useApi'
import type { SaveSmtpSettingsRequest, SmtpSettings, SmtpSettingsApi } from '../../types/smtpSettings'

/** The endpoint the panel's outgoing mail settings are read from and written to. */
const SMTP_PATH = '/api/v1/notifications/smtp'

/**
 * Builds the mail-settings API on top of the shared low-level client.
 *
 * The read carries no password and the type it returns has nowhere to put one, so
 * there is no call here that could ever hand a provider credential back to the
 * browser. What the screen gets instead is `hasPassword`.
 * @returns The {@link SmtpSettingsApi} bound to the panel's mail-settings endpoints.
 */
export const useSmtpSettingsApi = (): SmtpSettingsApi => {
  const api = useApi()

  /**
   * Reads the mail settings, with a flag in place of the password.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The stored settings, or blank ones on a panel that has never had any.
   */
  const get = (signal?: AbortSignal): Promise<SmtpSettings> => {
    return api.get<SmtpSettings>(SMTP_PATH, signal)
  }

  /**
   * Replaces the mail settings wholesale.
   * @param request The complete settings, with the password omitted to keep the stored one.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns True once they have been stored.
   */
  const save = (request: SaveSmtpSettingsRequest, signal?: AbortSignal): Promise<boolean> => {
    return api.put<boolean>(SMTP_PATH, request, signal)
  }

  /**
   * Sends one fixed test message, so an administrator can see whether the settings work.
   *
   * This is the one mail path that reports its failure to a caller, which is why the
   * screen renders the refusal's `detail` verbatim: the mail server's own words are
   * what an operator needs to fix their configuration.
   * @param recipient Where to send it.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns True once the message has been handed to the mail server.
   */
  const sendTest = (recipient: string, signal?: AbortSignal): Promise<boolean> => {
    return api.post<boolean>(`${SMTP_PATH}/test`, { recipient }, signal)
  }

  return { get, save, sendTest }
}
