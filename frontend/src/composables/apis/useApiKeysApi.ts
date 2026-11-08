import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type { ListRequest, Paged } from '../../types/list'
import type {
  ApiKey,
  ApiKeyChoice,
  ApiKeyCreated,
  ApiKeyOwner,
  ApiKeysApi,
  CreateApiKeyRequest,
  IssuingApiKeyOwner,
} from '../../types/apiKey'

/** The endpoint the administrator's own keys are listed, created and revoked through. Administrators only. */
const API_KEYS_PATH = '/api/v1/api-keys'

/** The endpoint a signed-in reseller's own keys are listed, created and revoked through. Resellers only. */
const RESELLER_API_KEYS_PATH = '/api/v1/reseller/api-keys'

/**
 * Names the endpoint family of an owner's keys: the administrator's own, the signed-in reseller's own,
 * or one reseller's as the administrator oversees them.
 * @param owner Whose keys.
 * @returns The path the owner's keys are listed and their choices read under (writes name their paths literally).
 */
const keysPathOf = (owner: ApiKeyOwner): string => {
  switch (owner.kind) {
    case 'administrator':
      return API_KEYS_PATH
    case 'reseller':
      return RESELLER_API_KEYS_PATH
    case 'overseen':
      return `/api/v1/resellers/${encodeURIComponent(owner.resellerId)}/api-keys`
  }
}

/** The list endpoint's parameter for each filter the API keys page reads from its URL. */
const LIST_FILTER_PARAMS: Readonly<Record<string, string>> = {
  status: 'status',
}

/**
 * Builds the API keys HTTP surface on top of the shared low-level client. Called from a store,
 * never from a component (rules/vue.md).
 *
 * Only `create` answers with a secret, and it is named for it. Nothing else in this file returns
 * one, and the value is never stored anywhere it could be read back.
 * @returns The {@link ApiKeysApi} bound to the panel's API key endpoints.
 */
export const useApiKeysApi = (): ApiKeysApi => {
  const api = useApi()

  /**
   * Reads one page of an owner's keys; the panel searches, filters, sorts and counts.
   * @param owner Whose keys.
   * @param request The page, size, search, sort and the status filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page, with the total across every page.
   */
  const list = (owner: ApiKeyOwner, request: ListRequest, signal?: AbortSignal): Promise<Paged<ApiKey>> => {
    return api.get<Paged<ApiKey>>(`${keysPathOf(owner)}${listQuery(request, LIST_FILTER_PARAMS)}`, signal)
  }

  /**
   * Creates a key for an issuing owner.
   * @param owner The administrator or the signed-in reseller; the panel stamps the owner from the token either way.
   * @param request The name, scopes, allowed ranges and expiry.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The key as stored and its secret, shown once.
   */
  const create = (owner: IssuingApiKeyOwner, request: CreateApiKeyRequest, signal?: AbortSignal): Promise<ApiKeyCreated> => {
    // One literal path per call site: the request-contract check (`maran api`) reads each write's path
    // statically, so a path computed from the owner would leave the body unchecked.
    return owner.kind === 'administrator'
      ? api.post<ApiKeyCreated>(API_KEYS_PATH, request, signal)
      : api.post<ApiKeyCreated>(RESELLER_API_KEYS_PATH, request, signal)
  }

  /**
   * Revokes a key; it stops authenticating at once and cannot be restored.
   * @param owner Whose key it is.
   * @param id The key to revoke.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel revoked the key; the answer is an empty 204.
   */
  const revoke = (owner: ApiKeyOwner, id: string, signal?: AbortSignal): Promise<void> => {
    // One literal path per call site, for the reason given at `create`.
    switch (owner.kind) {
      case 'administrator':
        return api.delete<void>(`${API_KEYS_PATH}/${id}`, signal)
      case 'reseller':
        return api.delete<void>(`${RESELLER_API_KEYS_PATH}/${id}`, signal)
      case 'overseen':
        return api.delete<void>(`/api/v1/resellers/${encodeURIComponent(owner.resellerId)}/api-keys/${id}`, signal)
    }
  }

  /**
   * Lists the scopes the issuing owner may grant a key, named by the panel.
   * @param owner The administrator or the signed-in reseller: each is offered its own set.
   * @param signal Optional abort signal.
   * @returns The scopes as `{ value, name }`.
   */
  const scopes = (owner: IssuingApiKeyOwner, signal?: AbortSignal): Promise<ApiKeyChoice[]> => {
    return api.get<ApiKeyChoice[]>(`${keysPathOf(owner)}/scopes`, signal)
  }

  /**
   * Lists the states a key can be filtered by, named by the panel.
   * @param owner Whose keys the filter is for. The states are the same for all of them, so the
   * oversight view asks the administrator's endpoint, which holds the one list.
   * @param signal Optional abort signal.
   * @returns The states as `{ value, name }`.
   */
  const statuses = (owner: ApiKeyOwner, signal?: AbortSignal): Promise<ApiKeyChoice[]> => {
    const path = owner.kind === 'overseen' ? API_KEYS_PATH : keysPathOf(owner)
    return api.get<ApiKeyChoice[]>(`${path}/statuses`, signal)
  }

  return { list, create, revoke, scopes, statuses }
}
