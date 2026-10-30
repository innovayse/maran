import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useAccountChoice } from './useAccountChoice'
import { useCallerRole } from './useCallerRole'
import type { AccountListFilter } from '../types/accountListFilter'

/** The `?account=<id>` narrowing a module list page reads from its URL. */
export interface AccountFilter {
  /** The filter to hand the store: `{ accountId }` when the URL names an account, `{}` otherwise. */
  filter: ComputedRef<AccountListFilter>
  /** Whether the URL narrows the list to one account. */
  isActive: ComputedRef<boolean>
  /**
   * The filter strip's text naming the account the list is narrowed to ("Only alpha"); the raw id
   * while the account list that names it has not arrived. Empty when the list is not narrowed.
   */
  text: ComputedRef<string>
  /**
   * Widens the list back to every account by dropping `account` from the URL, keeping the rest.
   * @returns Resolves once the route has changed.
   */
  clear: () => Promise<void>
}

/**
 * Reads the account a module list page is narrowed to from `?account=<id>`, the URL form the
 * account detail page links to. One composable for the five pages that take it, so the parameter
 * is read, ignored and cleared the same way everywhere.
 *
 * A customer ignores the parameter: they own one account and the server already scopes every list
 * to it, so a narrowing would add nothing but a strip naming their own account. This is
 * presentation only; the server ANDs the id with its tenant filter whoever sends it.
 * @returns The current filter, whether it is active, the strip's text, and the action that clears it.
 */
export const useAccountFilter = (): AccountFilter => {
  const route = useRoute()
  const router = useRouter()
  const { t } = useI18n()
  const { isCustomer } = useCallerRole()
  const { ownerName } = useAccountChoice()

  const filter = computed((): AccountListFilter => {
    const account = route.query.account
    return !isCustomer.value && typeof account === 'string' && account.length > 0 ? { accountId: account } : {}
  })

  const isActive = computed((): boolean => {
    return filter.value.accountId !== undefined
  })

  const text = computed((): string => {
    const id = filter.value.accountId
    if (id === undefined) {
      return ''
    }
    return t('common.accountFilter.only', { account: ownerName(id) ?? id })
  })

  /**
   * Drops `account` from the URL, keeping every other query parameter.
   * @returns Resolves once the route has changed.
   */
  const clear = async (): Promise<void> => {
    const query = { ...route.query }
    delete query.account
    await router.replace({ query })
  }

  return { filter, isActive, text, clear }
}
