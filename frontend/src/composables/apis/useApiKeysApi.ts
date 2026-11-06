import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type { ListRequest, Paged } from '../../types/list'
import type { ApiKey, ApiKeyChoice, ApiKeyCreated, ApiKeysApi, CreateApiKeyRequest } from '../../types/apiKey'

/** The endpoint API keys are listed and created through. Administrators only. */
const API_KEYS_PATH = '/api/v1/api-keys'

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
   * Reads one page of keys; the panel searches, filters, sorts and counts.
   * @param request The page, size, search, sort and the status filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page, with the total across every page.
   */
  const list = (request: ListRequest, signal?: AbortSignal): Promise<Paged<ApiKey>> => {
    return api.get<Paged<ApiKey>>(`${API_KEYS_PATH}${listQuery(request, LIST_FILTER_PARAMS)}`, signal)
  }

  /**
   * Creates a key.
   * @param request The name, scopes, allowed ranges and expiry.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The key as stored and its secret, shown once.
   */
  const create = (request: CreateApiKeyRequest, signal?: AbortSignal): Promise<ApiKeyCreated> => {
    return api.post<ApiKeyCreated>(API_KEYS_PATH, request, signal)
  }

  /**
   * Revokes a key; it stops authenticating at once and cannot be restored.
   * @param id The key to revoke.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel revoked the key; the answer is an empty 204.
   */
  const revoke = (id: string, signal?: AbortSignal): Promise<void> => {
    return api.delete<void>(`${API_KEYS_PATH}/${id}`, signal)
  }

  /**
   * Lists the scopes a key may be granted, named by the panel.
   * @param signal Optional abort signal.
   * @returns The scopes as `{ value, name }`.
   */
  const scopes = (signal?: AbortSignal): Promise<ApiKeyChoice[]> => {
    return api.get<ApiKeyChoice[]>(`${API_KEYS_PATH}/scopes`, signal)
  }

  /**
   * Lists the states a key can be filtered by, named by the panel.
   * @param signal Optional abort signal.
   * @returns The states as `{ value, name }`.
   */
  const statuses = (signal?: AbortSignal): Promise<ApiKeyChoice[]> => {
    return api.get<ApiKeyChoice[]>(`${API_KEYS_PATH}/statuses`, signal)
  }

  return { list, create, revoke, scopes, statuses }
}
