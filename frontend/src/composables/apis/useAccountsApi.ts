import { useApi } from '../useApi'
import type { Account, CreateAccountRequest } from '../../types/account'

/** The endpoint hosting accounts are listed and created through. */
const ACCOUNTS_PATH = '/api/v1/accounts'

/**
 * Typed access to the hosting accounts endpoints.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface AccountsApi {
  /**
   * Lists every hosting account.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The accounts the panel currently has.
   */
  list: (signal?: AbortSignal) => Promise<Account[]>

  /**
   * Creates a new hosting account row.
   * @param request The account's name, primary domain, and plan.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The created account.
   */
  create: (request: CreateAccountRequest, signal?: AbortSignal) => Promise<Account>
}

/**
 * Builds the accounts API on top of the shared low-level client.
 * @returns The {@link AccountsApi} bound to the panel's accounts endpoints.
 */
export const useAccountsApi = (): AccountsApi => {
  const api = useApi()

  return {
    list: (signal?: AbortSignal): Promise<Account[]> => api.get<Account[]>(ACCOUNTS_PATH, signal),
    create: (request: CreateAccountRequest, signal?: AbortSignal): Promise<Account> =>
      api.post<Account>(ACCOUNTS_PATH, request, signal),
  }
}
