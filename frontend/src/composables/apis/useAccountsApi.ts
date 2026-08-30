import { useApi } from '../useApi'
import type { Account, AccountsApi, CreateAccountRequest, Plan } from '../../types/account'

/** The endpoint hosting accounts are listed and created through. */
const ACCOUNTS_PATH = '/api/v1/accounts'

/**
 * Builds the accounts API on top of the shared low-level client.
 *
 * Each call is a named `const` arrow function with its own JSDoc rather than an
 * anonymous entry in the returned object: the name is what appears in a stack
 * trace, and the doc block sits next to the call it describes (rules/vue.md).
 * @returns The {@link AccountsApi} bound to the panel's accounts endpoints.
 */
export const useAccountsApi = (): AccountsApi => {
  const api = useApi()

  /**
   * Lists every hosting account the panel knows.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The accounts, in the order the backend reports them.
   */
  const list = (signal?: AbortSignal): Promise<Account[]> => {
    return api.get<Account[]>(ACCOUNTS_PATH, signal)
  }

  /**
   * Creates a hosting account.
   * @param request The account's name, primary domain and plan.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The account as the backend created it.
   */
  const create = (request: CreateAccountRequest, signal?: AbortSignal): Promise<Account> => {
    return api.post<Account>(ACCOUNTS_PATH, request, signal)
  }

  /**
   * Lists the plans an account can be created against.
   *
   * The form needs this so a person picks a plan by name instead of typing an
   * identifier: a free-text id field is a missing endpoint wearing a costume
   * (rules/architecture.md, "the backend owns the data").
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The plans, already localized by the backend.
   */
  const listPlans = (signal?: AbortSignal): Promise<Plan[]> => {
    return api.get<Plan[]>(`${ACCOUNTS_PATH}/plans`, signal)
  }

  return { list, create, listPlans }
}
