import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useAccountsStore } from '../stores/accounts'
import { useCronStore } from '../stores/cron'
import { useAccountChoice } from './useAccountChoice'
import { useAccountFilter } from './useAccountFilter'

/** What {@link useCronAccountStart} hands the scheduled tasks screen. */
export interface CronAccountStart {
  /**
   * The label of the account the screen is pointed at, for the picker: that account is usually not
   * on the page of matches the next search brings, and the picker must still name it.
   */
  selectedAccountLabel: ComputedRef<string | undefined>
  /**
   * Points the screen at the account the URL names, or at the first account the panel lists.
   * @returns Resolves once the account and its crontab have settled.
   */
  start: () => Promise<void>
}

/**
 * Decides which account the scheduled tasks screen opens on (`?account=<id>`, the link the account
 * detail page draws), or the first account the panel lists.
 *
 * The first rather than none, because a screen that opens on an empty picker looks broken; there is
 * nothing invented in it — the account came from the panel, and the crontab shown is that account's.
 * A URL account is selected only once the panel has named it by its own read: an id it cannot name
 * is ignored, since the picker could not show it and the server would answer 404 for it anyway. A
 * customer reads no choices (an administrator-only read) and is pointed at their own account; the
 * parameter means nothing to them.
 * @returns The picker's selected label and the start action.
 */
export const useCronAccountStart = (): CronAccountStart => {
  const store = useCronStore()
  const accountsStore = useAccountsStore()
  const { fixedAccountId, picker: accountPicker, search: searchAccounts } = useAccountChoice()
  const { filter: accountListFilter } = useAccountFilter()

  /** The name of the first listed account when the screen opened on it, as the choices named it. */
  const firstAccountLabel: Ref<string | undefined> = ref(undefined)

  /**
   * The account the picker names: its known name, else the first listed account's label.
   */
  const selectedAccountLabel: ComputedRef<string | undefined> = computed(() => {
    return accountsStore.accountNames[store.accountId] ?? firstAccountLabel.value
  })

  /**
   * Points the screen at the account described in the composable's comment.
   * @returns Resolves once the account and its crontab have settled.
   */
  const start = async (): Promise<void> => {
    const own = fixedAccountId.value
    if (own !== null) {
      if (store.accountId !== own) {
        await store.selectAccount(own)
      }
      return
    }
    const requested = accountListFilter.value.accountId
    if (requested !== undefined) {
      await accountsStore.loadAccountName(requested)
      if (accountsStore.accountNames[requested] !== undefined) {
        // A link to one account's crontab wins over whichever account the store last looked at.
        if (store.accountId !== requested) {
          await store.selectAccount(requested)
        }
        return
      }
    }
    if (store.accountId !== '') {
      // Back on the account the store last looked at, which the picker must still name.
      await accountsStore.loadAccountName(store.accountId)
      return
    }
    await searchAccounts('')
    const first = accountPicker.value.options[0]
    if (first !== undefined) {
      firstAccountLabel.value = first.label
      await store.selectAccount(first.value)
    }
  }

  return { selectedAccountLabel, start }
}
