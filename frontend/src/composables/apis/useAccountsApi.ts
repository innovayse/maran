import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type {
  Account,
  AccountDetail,
  AccountOption,
  AccountsApi,
  ChangeAccountOwnerEmailRequest,
  ChangeAccountPlanRequest,
  CreateAccountRequest,
  MyAccount,
  Plan,
} from '../../types/account'
import type { AccountResourceSummary } from '../../types/accountResourceSummary'
import type { ListRequest, Paged } from '../../types/list'

/** The endpoint hosting accounts are listed and created through. */
const ACCOUNTS_PATH = '/api/v1/accounts'

/** The endpoint a picker reads account names through: identity and name only, one page at a time. */
const ACCOUNT_CHOICES_PATH = '/api/v1/accounts/choices'

/** The list endpoint's parameter for each filter the accounts list page reads from its URL. */
const LIST_FILTER_PARAMS: Readonly<Record<string, string>> = { plan: 'planId', status: 'status' }

/** The endpoint a customer's own account, plan and limits are read through. */
const MY_ACCOUNT_PATH = '/api/v1/accounts/me'

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
   * Lists one page of hosting accounts.
   * @param request The page, size, search, sort and the `plan` and `status` filters.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered.
   */
  const list = (request: ListRequest, signal?: AbortSignal): Promise<Paged<Account>> => {
    return api.get<Paged<Account>>(`${ACCOUNTS_PATH}${listQuery(request, LIST_FILTER_PARAMS)}`, signal)
  }

  /**
   * Lists the accounts a picker offers: the first page of those matching the search.
   * @param search Narrows by name; empty for the first accounts in the panel's order.
   * @param pageSize How many choices to ask for; one of the sizes the panel accepts.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The matching accounts as identity and name, with how many match in all.
   */
  const choices = (search: string, pageSize: number, signal?: AbortSignal): Promise<Paged<AccountOption>> => {
    const params = new URLSearchParams({ pageSize: String(pageSize) })
    // An empty search is left out: the panel refuses an empty parameter rather than reading it as none.
    if (search !== '') {
      params.set('search', search)
    }
    return api.get<Paged<AccountOption>>(`${ACCOUNT_CHOICES_PATH}?${params.toString()}`, signal)
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

  /**
   * Reads one account.
   * @param id The account's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The account, as the panel currently has it.
   */
  const get = (id: string, signal?: AbortSignal): Promise<AccountDetail> => {
    return api.get<AccountDetail>(`${ACCOUNTS_PATH}/${id}`, signal)
  }

  /**
   * Moves an account to another plan.
   * @param id The account's identity.
   * @param request The plan to move it to.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The account as it now stands.
   */
  const changePlan = (id: string, request: ChangeAccountPlanRequest, signal?: AbortSignal): Promise<AccountDetail> => {
    return api.put<AccountDetail>(`${ACCOUNTS_PATH}/${id}/plan`, request, signal)
  }

  /**
   * Changes an account's owner address.
   * @param id The account's identity.
   * @param request The new address.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The account as it now stands.
   */
  const changeOwnerEmail = (
    id: string,
    request: ChangeAccountOwnerEmailRequest,
    signal?: AbortSignal,
  ): Promise<AccountDetail> => {
    return api.put<AccountDetail>(`${ACCOUNTS_PATH}/${id}/owner-email`, request, signal)
  }

  /**
   * Suspends an account.
   * @param id The account's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The account in its new state.
   */
  const suspend = (id: string, signal?: AbortSignal): Promise<Account> => {
    return api.post<Account>(`${ACCOUNTS_PATH}/${id}/suspend`, undefined, signal)
  }

  /**
   * Lifts a suspension.
   * @param id The account's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The account in its new state.
   */
  const reactivate = (id: string, signal?: AbortSignal): Promise<Account> => {
    return api.post<Account>(`${ACCOUNTS_PATH}/${id}/reactivate`, undefined, signal)
  }

  /**
   * Deletes an account together with its system user and home directory.
   * @param id The account's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The bytes the deletion reclaimed.
   */
  const remove = (id: string, signal?: AbortSignal): Promise<number> => {
    return api.delete<number>(`${ACCOUNTS_PATH}/${id}`, signal)
  }

  /**
   * Reads the account the caller owns.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The caller's own account, its status, and its plan's limits.
   */
  const getMine = (signal?: AbortSignal): Promise<MyAccount> => {
    return api.get<MyAccount>(MY_ACCOUNT_PATH, signal)
  }

  /**
   * Reads what one account holds against its plan. Administrator-only on the server.
   * @param id The account's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns One row per resource kind this server counts.
   */
  const getResourceSummary = (id: string, signal?: AbortSignal): Promise<AccountResourceSummary> => {
    return api.get<AccountResourceSummary>(`${ACCOUNTS_PATH}/${id}/resource-summary`, signal)
  }

  /**
   * Reads what the caller's own account holds against its plan.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns One row per resource kind this server counts.
   */
  const getMyResourceSummary = (signal?: AbortSignal): Promise<AccountResourceSummary> => {
    return api.get<AccountResourceSummary>(`${MY_ACCOUNT_PATH}/resource-summary`, signal)
  }

  return {
    list,
    choices,
    create,
    listPlans,
    get,
    changePlan,
    changeOwnerEmail,
    suspend,
    reactivate,
    remove,
    getMine,
    getResourceSummary,
    getMyResourceSummary,
  }
}
