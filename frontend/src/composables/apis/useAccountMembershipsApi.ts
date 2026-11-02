import { useApi } from '../useApi'
import type {
  AccountMembership,
  AccountMembershipsApi,
  SwitchAccountRequest,
  SwitchedAccount,
} from '../../types/accountMembership'

/** The endpoint the caller's own accounts are listed at. */
const ACCOUNTS_PATH = '/api/v1/me/accounts'

/** The endpoint the account a sign-in acts for is switched at. */
const ACTIVE_ACCOUNT_PATH = '/api/v1/me/active-account'

/**
 * Builds the account memberships API on top of the shared low-level client. Both endpoints are
 * scoped to the caller's own token, so no call here names a user.
 * @returns The {@link AccountMembershipsApi} bound to the `me` endpoints.
 */
export const useAccountMembershipsApi = (): AccountMembershipsApi => {
  const api = useApi()

  /**
   * Reads the accounts the caller holds a membership of.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns One row per account, the active one marked.
   */
  const list = (signal?: AbortSignal): Promise<AccountMembership[]> => {
    return api.get<AccountMembership[]>(ACCOUNTS_PATH, signal)
  }

  /**
   * Makes the caller's current sign-in act for another account.
   * @param request The account to act for.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The token that names the chosen account.
   */
  const switchTo = (request: SwitchAccountRequest, signal?: AbortSignal): Promise<SwitchedAccount> => {
    return api.post<SwitchedAccount>(ACTIVE_ACCOUNT_PATH, request, signal)
  }

  return { list, switchTo }
}
