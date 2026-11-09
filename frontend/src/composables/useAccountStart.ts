import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useAccountsStore } from '../stores/accounts'
import type { AccountChoice } from './useAccountChoice'
import type { ServerTable } from './useServerTable'

/** What {@link useAccountStart} hands a screen that lists one account's files. */
export interface AccountStart {
  /** The label the account picker shows for the chosen account, which may not be on its page of matches. */
  selectedAccountLabel: ComputedRef<string | undefined>
  /**
   * Points an administrator at an account when the URL names none: the first one the panel lists,
   * because a screen that opens on an empty picker looks broken. Nothing is invented in it, the
   * account came from the panel. A customer is never offered a choice and nothing happens.
   * @returns Resolves once the account is known.
   */
  start: () => Promise<void>
}

/**
 * Settles which account a file screen opens on. The browser and the trash both list one account's
 * data, take it from the `account` filter of their `useServerTable`, and share this behaviour, so it
 * lives in one place.
 * @param table The screen's server table, built with an `account` filter.
 * @param choice The screen's account choice.
 * @param accountId The account on screen: a customer's own, else the one the URL names.
 * @returns The picker's label for the chosen account and the action that picks the first one.
 */
export const useAccountStart = (
  table: ServerTable,
  choice: AccountChoice,
  accountId: ComputedRef<string>,
): AccountStart => {
  const accountsStore = useAccountsStore()

  /** The label of the first listed account when the screen opened on it, as the choices named it. */
  const firstAccountLabel: Ref<string | undefined> = ref(undefined)

  const selectedAccountLabel: ComputedRef<string | undefined> = computed(() => {
    return accountsStore.accountNames[accountId.value] ?? firstAccountLabel.value
  })

  /**
   * Points an administrator at an account when the URL names none.
   * @returns Resolves once the account is known.
   */
  const start = async (): Promise<void> => {
    if (choice.fixedAccountId.value !== null) {
      return
    }
    const requested = table.filters.value.account
    if (requested !== '') {
      await accountsStore.loadAccountName(requested)
      return
    }
    await choice.search('')
    const first = choice.picker.value.options[0]
    if (first !== undefined) {
      firstAccountLabel.value = first.label
      await table.setFilters({ ...table.filters.value, account: first.value })
    }
  }

  return { selectedAccountLabel, start }
}
