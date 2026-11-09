import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type { ListRequest, Paged } from '../../types/list'
import type {
  ChangeResellerEnvelopeRequest,
  CreateResellerRequest,
  Reseller,
  ResellerChoice,
  ResellerDetail,
  ResellersApi,
  UpdateResellerRequest,
} from '../../types/reseller'

/** The endpoint resellers are listed, created and managed through (administrator-only). */
const RESELLERS_PATH = '/api/v1/resellers'

/** The endpoint a picker reads reseller names through: identity and name only, one page at a time. */
const RESELLER_CHOICES_PATH = '/api/v1/resellers/choices'

/** The endpoint a reseller reads its own profile, envelope and usage through. */
const MY_RESELLER_PATH = '/api/v1/reseller/me'

/** The list endpoint's parameter for each filter the resellers list page reads from its URL. */
const LIST_FILTER_PARAMS: Readonly<Record<string, string>> = { status: 'status' }

/**
 * Builds the resellers API on top of the shared low-level client.
 *
 * Each call is a named `const` arrow function with its own JSDoc rather than an anonymous entry in
 * the returned object: the name is what appears in a stack trace, and the doc block sits next to
 * the call it describes (rules/vue.md).
 * @returns The {@link ResellersApi} bound to the panel's reseller endpoints.
 */
export const useResellersApi = (): ResellersApi => {
  const api = useApi()

  /**
   * Lists one page of resellers.
   * @param request The page, size, search, sort and the `status` filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered.
   */
  const list = (request: ListRequest, signal?: AbortSignal): Promise<Paged<Reseller>> => {
    return api.get<Paged<Reseller>>(`${RESELLERS_PATH}${listQuery(request, LIST_FILTER_PARAMS)}`, signal)
  }

  /**
   * Lists the resellers a picker offers: the first page of those matching the search.
   * @param search Narrows by name; empty for the first resellers in the panel's order.
   * @param pageSize How many choices to ask for; one of the sizes the panel accepts.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The matching resellers as identity and name, with how many match in all.
   */
  const choices = (search: string, pageSize: number, signal?: AbortSignal): Promise<Paged<ResellerChoice>> => {
    const params = new URLSearchParams({ pageSize: String(pageSize) })
    // An empty search is left out: the panel refuses an empty parameter rather than reading it as none.
    if (search !== '') {
      params.set('search', search)
    }
    return api.get<Paged<ResellerChoice>>(`${RESELLER_CHOICES_PATH}?${params.toString()}`, signal)
  }

  /**
   * Reads one reseller.
   * @param id The reseller's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The reseller with its envelope and usage.
   */
  const get = (id: string, signal?: AbortSignal): Promise<ResellerDetail> => {
    return api.get<ResellerDetail>(`${RESELLERS_PATH}/${id}`, signal)
  }

  /**
   * Reads the reseller the caller is.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The caller's own reseller with its envelope and usage.
   */
  const getMine = (signal?: AbortSignal): Promise<ResellerDetail> => {
    return api.get<ResellerDetail>(MY_RESELLER_PATH, signal)
  }

  /**
   * Creates a reseller.
   * @param request The reseller's name, contact address, language and envelope.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The reseller as the panel created it.
   */
  const create = (request: CreateResellerRequest, signal?: AbortSignal): Promise<ResellerDetail> => {
    return api.post<ResellerDetail>(RESELLERS_PATH, request, signal)
  }

  /**
   * Renames a reseller and replaces its contact address and language.
   * @param id The reseller's identity.
   * @param request The reseller's new name, contact address and language.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The reseller as it now stands.
   */
  const update = (id: string, request: UpdateResellerRequest, signal?: AbortSignal): Promise<ResellerDetail> => {
    return api.put<ResellerDetail>(`${RESELLERS_PATH}/${id}`, request, signal)
  }

  /**
   * Replaces a reseller's envelope.
   * @param id The reseller's identity.
   * @param request The new envelope.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The reseller as it now stands.
   */
  const changeEnvelope = (
    id: string,
    request: ChangeResellerEnvelopeRequest,
    signal?: AbortSignal,
  ): Promise<ResellerDetail> => {
    return api.put<ResellerDetail>(`${RESELLERS_PATH}/${id}/envelope`, request, signal)
  }

  /**
   * Suspends a reseller.
   * @param id The reseller's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The reseller in its new state.
   */
  const suspend = (id: string, signal?: AbortSignal): Promise<Reseller> => {
    return api.post<Reseller>(`${RESELLERS_PATH}/${id}/suspend`, undefined, signal)
  }

  /**
   * Lifts a suspension.
   * @param id The reseller's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The reseller in its new state.
   */
  const reactivate = (id: string, signal?: AbortSignal): Promise<Reseller> => {
    return api.post<Reseller>(`${RESELLERS_PATH}/${id}/reactivate`, undefined, signal)
  }

  /**
   * Deletes a reseller.
   * @param id The reseller's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the reseller was deleted; the panel answers 204 with no body.
   */
  const remove = (id: string, signal?: AbortSignal): Promise<void> => {
    return api.delete<void>(`${RESELLERS_PATH}/${id}`, signal)
  }

  return { list, choices, get, getMine, create, update, changeEnvelope, suspend, reactivate, remove }
}
