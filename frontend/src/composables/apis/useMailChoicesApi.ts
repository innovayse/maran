import { useApi } from '../useApi'
import type { MailChoices, MailChoicesApi } from '../../types/mailChoice'

/** The endpoint the mail screens' closed sets and bounds are read from. */
const MAIL_CHOICES_PATH = '/api/v1/mail/choices'

/**
 * Builds the mail choices API on top of the shared low-level client.
 * @returns The {@link MailChoicesApi} bound to the panel's mail choices endpoint.
 */
export const useMailChoicesApi = (): MailChoicesApi => {
  const api = useApi()

  /**
   * Reads the sets and bounds the mail screens need, named in the caller's language.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The choices.
   */
  const get = (signal?: AbortSignal): Promise<MailChoices> => {
    return api.get<MailChoices>(MAIL_CHOICES_PATH, signal)
  }

  return { get }
}
