import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type { ListRequest, Paged } from '../../types/list'
import type {
  ChangeMailboxPasswordRequest,
  CreateMailboxRequest,
  Mailbox,
  MailboxesApi,
  UpdateMailboxRequest,
} from '../../types/mailbox'

/** The endpoint mailboxes are listed and created through. */
const MAILBOXES_PATH = '/api/v1/mailboxes'

/** The list endpoint's parameter for each filter the mailboxes page reads from its URL. */
const LIST_FILTER_PARAMS: Readonly<Record<string, string>> = {
  account: 'accountId',
  domain: 'domain',
  status: 'status',
}

/**
 * Builds the mailboxes API on top of the shared low-level client.
 *
 * Two calls carry a password, `create` and `changePassword`, and neither returns one: the server
 * keeps a hash and there is nothing to show back.
 * @returns The {@link MailboxesApi} bound to the panel's mailbox endpoints.
 */
export const useMailboxesApi = (): MailboxesApi => {
  const api = useApi()

  /**
   * Lists one page of the mailboxes the caller may see.
   * @param request The page, size, search, sort and the account, domain and status filters.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered, with the total across every page.
   */
  const list = (request: ListRequest, signal?: AbortSignal): Promise<Paged<Mailbox>> => {
    return api.get<Paged<Mailbox>>(`${MAILBOXES_PATH}${listQuery(request, LIST_FILTER_PARAMS)}`, signal)
  }

  /**
   * Creates a mailbox.
   * @param request The domain, local part, password and quota.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The mailbox as created.
   */
  const create = (request: CreateMailboxRequest, signal?: AbortSignal): Promise<Mailbox> => {
    return api.post<Mailbox>(MAILBOXES_PATH, request, signal)
  }

  /**
   * Changes a mailbox's quota, send limit and on/off state.
   * @param id The mailbox to change.
   * @param request The new limits and state.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The mailbox as stored.
   */
  const update = (id: string, request: UpdateMailboxRequest, signal?: AbortSignal): Promise<Mailbox> => {
    return api.put<Mailbox>(`${MAILBOXES_PATH}/${encodeURIComponent(id)}`, request, signal)
  }

  /**
   * Sets a new password.
   * @param id The mailbox to re-credential.
   * @param request The new password.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel stored it; the answer is an empty 204.
   */
  const changePassword = (id: string, request: ChangeMailboxPasswordRequest, signal?: AbortSignal): Promise<void> => {
    return api.put<void>(`${MAILBOXES_PATH}/${encodeURIComponent(id)}/password`, request, signal)
  }

  /**
   * Removes the mailbox and the mail it holds.
   * @param id The mailbox to remove.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel removed it; the answer is an empty 204.
   */
  const remove = (id: string, signal?: AbortSignal): Promise<void> => {
    return api.delete<void>(`${MAILBOXES_PATH}/${encodeURIComponent(id)}`, signal)
  }

  return { list, create, update, changePassword, remove }
}
