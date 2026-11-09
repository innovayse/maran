import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type { ListRequest, Paged } from '../../types/list'
import type {
  CreateMailDomainRequest,
  MailDnsRecord,
  MailDomain,
  MailDomainsApi,
} from '../../types/mailDomain'

/** The endpoint mail domains are listed and created through. */
const MAIL_DOMAINS_PATH = '/api/v1/mail-domains'

/** The list endpoint's parameter for each filter the mail domains page reads from its URL. */
const LIST_FILTER_PARAMS: Readonly<Record<string, string>> = {
  account: 'accountId',
  status: 'status',
}

/**
 * Builds the mail domains API on top of the shared low-level client.
 *
 * Each call is a named `const` arrow function with its own JSDoc rather than an anonymous entry in
 * the returned object (rules/vue.md).
 * @returns The {@link MailDomainsApi} bound to the panel's mail domain endpoints.
 */
export const useMailDomainsApi = (): MailDomainsApi => {
  const api = useApi()

  /**
   * Lists one page of the mail domains the caller may see.
   * @param request The page, size, search, sort and the account and status filters.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered, with the total across every page.
   */
  const list = (request: ListRequest, signal?: AbortSignal): Promise<Paged<MailDomain>> => {
    return api.get<Paged<MailDomain>>(`${MAIL_DOMAINS_PATH}${listQuery(request, LIST_FILTER_PARAMS)}`, signal)
  }

  /**
   * Adds a mail domain.
   * @param request The owning account and the domain name.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The domain as created.
   */
  const create = (request: CreateMailDomainRequest, signal?: AbortSignal): Promise<MailDomain> => {
    return api.post<MailDomain>(MAIL_DOMAINS_PATH, request, signal)
  }

  /**
   * Removes the domain. A domain belonging to somebody else answers 404, never 403.
   * @param id The domain to remove.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel removed the domain; the answer is an empty 204.
   */
  const remove = (id: string, signal?: AbortSignal): Promise<void> => {
    return api.delete<void>(`${MAIL_DOMAINS_PATH}/${encodeURIComponent(id)}`, signal)
  }

  /**
   * Starts a new DKIM key for the domain.
   * @param id The domain whose key rotates.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The domain with its new selector.
   */
  const rotateDkim = (id: string, signal?: AbortSignal): Promise<MailDomain> => {
    return api.post<MailDomain>(`${MAIL_DOMAINS_PATH}/${encodeURIComponent(id)}/dkim-rotation`, undefined, signal)
  }

  /**
   * Reads the DNS records to publish for the domain.
   * @param id The domain.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The records, in the order they should be entered.
   */
  const dnsRecords = (id: string, signal?: AbortSignal): Promise<MailDnsRecord[]> => {
    return api.get<MailDnsRecord[]>(`${MAIL_DOMAINS_PATH}/${encodeURIComponent(id)}/dns-records`, signal)
  }

  return { list, create, remove, rotateDkim, dnsRecords }
}
