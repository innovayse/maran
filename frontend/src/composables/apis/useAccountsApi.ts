import { useApi } from '../useApi'
import type { Account, AccountsApi, CreateAccountRequest } from '../../types/account'

/** The endpoint hosting accounts are listed and created through. */
const ACCOUNTS_PATH = '/api/v1/accounts'

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
