import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type { ListRequest, Paged } from '../../types/list'
import type {
  CreateMailAliasRequest,
  MailAlias,
  MailAliasesApi,
  UpdateMailAliasRequest,
} from '../../types/mailAlias'

/** The endpoint aliases are listed and created through. */
const MAIL_ALIASES_PATH = '/api/v1/mail-aliases'

/** The list endpoint's parameter for each filter the aliases page reads from its URL. */
const LIST_FILTER_PARAMS: Readonly<Record<string, string>> = {
  account: 'accountId',
  domain: 'domain',
  kind: 'kind',
}

/**
 * Builds the mail aliases API on top of the shared low-level client.
 * @returns The {@link MailAliasesApi} bound to the panel's alias endpoints.
 */
export const useMailAliasesApi = (): MailAliasesApi => {
  const api = useApi()

  /**
   * Lists one page of the aliases the caller may see.
   * @param request The page, size, search, sort and the account, domain and kind filters.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered, with the total across every page.
   */
  const list = (request: ListRequest, signal?: AbortSignal): Promise<Paged<MailAlias>> => {
    return api.get<Paged<MailAlias>>(`${MAIL_ALIASES_PATH}${listQuery(request, LIST_FILTER_PARAMS)}`, signal)
  }

  /**
   * Creates an alias, forwarder or catch-all.
   * @param request The domain, kind, local part and targets.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The alias as created.
   */
  const create = (request: CreateMailAliasRequest, signal?: AbortSignal): Promise<MailAlias> => {
    return api.post<MailAlias>(MAIL_ALIASES_PATH, request, signal)
  }

  /**
   * Replaces an alias's destinations.
   * @param id The alias to change.
   * @param request The new destinations.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The alias as stored.
   */
  const update = (id: string, request: UpdateMailAliasRequest, signal?: AbortSignal): Promise<MailAlias> => {
    return api.put<MailAlias>(`${MAIL_ALIASES_PATH}/${encodeURIComponent(id)}`, request, signal)
  }

  /**
   * Removes the alias.
   * @param id The alias to remove.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel removed it; the answer is an empty 204.
   */
  const remove = (id: string, signal?: AbortSignal): Promise<void> => {
    return api.delete<void>(`${MAIL_ALIASES_PATH}/${encodeURIComponent(id)}`, signal)
  }

  return { list, create, update, remove }
}
