import { computed, watch, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useAccountsStore } from '../stores/accounts'
import { useCallerRole } from './useCallerRole'
import type { ListFilterSpec } from './useServerTable'
import type { AccountListFilter } from '../types/accountListFilter'

/** The `?account=<id>` narrowing a module list page reads from its URL. */
export interface AccountFilter {
  /** The filter to hand the store: `{ accountId }` when the URL names an account, `{}` otherwise. */
  filter: ComputedRef<AccountListFilter>
  /** Whether the URL narrows the list to one account. */
  isActive: ComputedRef<boolean>
  /**
   * The name of the account the list is narrowed to, read by id from the panel; the raw id while
   * that read is in flight or if it failed. Empty when the list is not narrowed.
   */
  name: ComputedRef<string>
  /**
   * The filter strip's text naming the account the list is narrowed to ("Only alpha"), built from
   * {@link name}. Empty when the list is not narrowed.
   */
  text: ComputedRef<string>
  /**
   * Widens the list back to every account by dropping `account` from the URL, keeping the rest.
   * @returns Resolves once the route has changed.
   */
  clear: () => Promise<void>
  /**
   * The `account` entry for a `useServerTable` filter schema (`filters: { account: spec }`), so a
   * server-paged list reads and ignores the parameter exactly as this composable does.
   */
  spec: ListFilterSpec
}

/**
 * Reads the account a module list page is narrowed to from `?account=<id>`, the URL form the
 * account detail page links to. One composable for the five pages that take it, so the parameter
 * is read, ignored and cleared the same way everywhere.
 *
 * A customer ignores the parameter: they own one account and the server already scopes every list
 * to it, so a narrowing would add nothing but a strip naming their own account. This is
 * presentation only; the server ANDs the id with its tenant filter whoever sends it.
 *
 * It stays a composable of its own rather than becoming a view over `useServerTable`: the pages
 * that take `?account=` are not all server-paged lists, and the strip's text needs the account's
 * name, which the table has no business knowing. The name is read by id (`GET /api/v1/accounts/{id}`,
 * administrator-only, which is fine: the parameter is ignored for a customer) and cached per id. A paged list shares the parameter through
 * {@link AccountFilter.spec}, so both read the same key with the same rule.
 * @returns The current filter, whether it is active, the account's name, the strip's text, the action that clears it,
 * and the schema entry for a server-paged list.
 */
export const useAccountFilter = (): AccountFilter => {
  const route = useRoute()
  const router = useRouter()
  const { t } = useI18n()
  const { isCustomer } = useCallerRole()
  const accountsStore = useAccountsStore()

  const filter = computed((): AccountListFilter => {
    const account = route.query.account
    return !isCustomer.value && typeof account === 'string' && account.length > 0 ? { accountId: account } : {}
  })

  const isActive = computed((): boolean => {
    return filter.value.accountId !== undefined
  })

  const name = computed((): string => {
    const id = filter.value.accountId
    if (id === undefined) {
      return ''
    }
    return accountsStore.accountNames[id] ?? id
  })

  const text = computed((): string => {
    return isActive.value ? t('common.accountFilter.only', { account: name.value }) : ''
  })

  const spec: ListFilterSpec = {
    /**
     * Refuses the parameter for a customer, whose lists the server already scopes to one account.
     * @returns True when the caller may narrow by account.
     */
    accepts: (): boolean => {
      return !isCustomer.value
    },
  }

  /**
   * Drops `account` from the URL, keeping every other query parameter.
   * @returns Resolves once the route has changed.
   */
  const clear = async (): Promise<void> => {
    const query = { ...route.query }
    delete query.account
    await router.replace({ query })
  }

  // The account is named by its own read, not looked up in a page of choices: the narrowed account
  // can be any of thousands, and a lookup in one page would print its id past the first page.
  watch(
    (): string | undefined => {
      return filter.value.accountId
    },
    (id: string | undefined): void => {
      if (id !== undefined) {
        void accountsStore.loadAccountName(id)
      }
    },
    { immediate: true },
  )

  return { filter, isActive, name, text, clear, spec }
}
