import type { AccountListFilter } from '../types/accountListFilter'

/**
 * Builds the query string a module's list request carries for an account filter, escaped rather
 * than concatenated. Shared by every list endpoint that takes `?accountId=`, so the parameter has
 * one spelling across the SPA.
 * @param filter The account the list is narrowed to, if any.
 * @returns The query string including its leading `?`, or `''` when the list is not narrowed.
 */
export const accountListQuery = (filter: AccountListFilter): string => {
  return filter.accountId === undefined ? '' : `?${new URLSearchParams({ accountId: filter.accountId }).toString()}`
}
