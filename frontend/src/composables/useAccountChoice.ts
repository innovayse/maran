import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useAccountsStore } from '../stores/accounts'
import { useAuthStore } from '../stores/auth'
import { useCallerRole } from './useCallerRole'

/** What an account picker (`UiCombobox`) is bound to: the current page of matches and its state. */
export interface AccountPickerState {
  /** The accounts matching what was last typed, as the picker lists them: identity and name. */
  options: { value: string; label: string }[]
  /** How many accounts match in all; more than {@link options} holds when the page is cut short. */
  total: number
  /** Whether the read for what was typed is still in flight. */
  loading: boolean
  /** Why the read failed, already localized; `null` while it has not failed. */
  loadError: string | null
  /** The accessible name of the picker's search field, translated. */
  searchLabel: string
}

/** Which account a create form targets, and the search-driven picker an administrator chooses with. */
export interface AccountChoice {
  /**
   * The account every request is for when the caller has no choice to make: a customer's own
   * account, from their token. `null` for an administrator, who picks one.
   */
  fixedAccountId: ComputedRef<string | null>
  /** The picker's bindings (`v-bind` on `UiCombobox`), read from the open picker's latest search. */
  picker: ComputedRef<AccountPickerState>
  /**
   * Reads the accounts matching a search into {@link picker}; bound to `UiCombobox`'s `search`.
   * Does nothing for a customer, who never reads the administrator-only choices.
   * @param term What was typed; empty for the first accounts the panel lists.
   * @returns Resolves once the read has settled.
   */
  search: (term: string) => Promise<void>
}

/**
 * Decides how a screen chooses an account. A customer owns exactly one, so they are never shown a
 * picker and never send `GET /api/v1/accounts/choices` (an administrator-only endpoint); their own
 * account id goes on every create automatically. An administrator searches the account choices: one
 * page of identity and name per search, never a whole list held in the browser, so a panel with
 * thousands of accounts offers every one of them through the search.
 *
 * Owner names on list rows are not read here: each row carries the panel's `ownerName`.
 *
 * Presentation only: every endpoint checks the account id it is given against the caller's tenant
 * and answers 404 for anyone else's, whatever this composable decided.
 * @returns The fixed account, the picker's bindings and its search.
 */
export const useAccountChoice = (): AccountChoice => {
  const { t } = useI18n()
  const authStore = useAuthStore()
  const accountsStore = useAccountsStore()
  const { isCustomer } = useCallerRole()

  const fixedAccountId = computed((): string | null => {
    return isCustomer.value ? (authStore.user?.accountId ?? null) : null
  })

  const picker = computed((): AccountPickerState => {
    const searchLabel = t('common.list.search')
    const read = accountsStore.choicePage
    if (read.kind === 'loaded') {
      const options = read.page.items.map((account): { value: string; label: string } => {
        return { value: account.id, label: account.name }
      })
      return { options, total: read.page.total, loading: false, loadError: null, searchLabel }
    }
    if (read.kind === 'refused') {
      return { options: [], total: 0, loading: false, loadError: read.message, searchLabel }
    }
    if (read.kind === 'unreachable') {
      return { options: [], total: 0, loading: false, loadError: t('common.list.unreachable'), searchLabel }
    }
    return { options: [], total: 0, loading: true, loadError: null, searchLabel }
  })

  /**
   * Reads the choices matching a search, for an administrator only.
   * @param term What was typed into the picker.
   * @returns Resolves once the read has settled.
   */
  const search = async (term: string): Promise<void> => {
    if (isCustomer.value) {
      return
    }
    await accountsStore.searchChoices(term)
  }

  return { fixedAccountId, picker, search }
}
