import { computed, type ComputedRef } from 'vue'
import { useAccountsStore } from '../stores/accounts'
import { useAuthStore } from '../stores/auth'
import { useCallerRole } from './useCallerRole'
import type { Account } from '../types/account'

/** Which account a create form targets, and the names a list shows for its rows' owners. */
export interface AccountChoice {
  /**
   * The account every request is for when the caller has no choice to make: a customer's own
   * account, from their token. `null` for an administrator, who picks one.
   */
  fixedAccountId: ComputedRef<string | null>
  /** The accounts an administrator may pick from; always empty for a customer, who picks nothing. */
  accounts: ComputedRef<readonly Account[]>
  /**
   * Names the account a row belongs to.
   * @param id The owning account's identity.
   * @returns The account's short name, or `null` when this caller's lists do not name it.
   */
  ownerName: (id: string) => string | null
  /**
   * Loads what the screen needs to name accounts: the caller's own account for a customer, the full
   * list for an administrator.
   * @returns Resolves once the read has settled.
   */
  load: () => Promise<void>
}

/**
 * Decides how a screen chooses an account. A customer owns exactly one, so they are never shown a
 * picker and never send `GET /api/v1/accounts` (an administrator-only endpoint); their own account
 * id goes on every create automatically. An administrator keeps the picker over the full list.
 *
 * Presentation only: every endpoint checks the account id it is given against the caller's tenant
 * and answers 404 for anyone else's, whatever this composable decided.
 * @returns The fixed account, the pickable accounts, an owner-name lookup and the loader.
 */
export const useAccountChoice = (): AccountChoice => {
  const authStore = useAuthStore()
  const accountsStore = useAccountsStore()
  const { isCustomer } = useCallerRole()

  const fixedAccountId = computed((): string | null => {
    return isCustomer.value ? (authStore.user?.accountId ?? null) : null
  })

  const accounts = computed((): readonly Account[] => {
    return isCustomer.value ? [] : accountsStore.accounts
  })

  /**
   * Names the account a row belongs to, from whichever list this caller is allowed to read.
   * @param id The owning account's identity.
   * @returns The account's short name, or `null` when it is not known here.
   */
  const ownerName = (id: string): string | null => {
    if (isCustomer.value) {
      const mine = accountsStore.mine
      return mine !== null && mine.id === id ? mine.name : null
    }
    const owner = accountsStore.accounts.find((account) => {
      return account.id === id
    })
    return owner?.name ?? null
  }

  /**
   * Reads the caller's own account for a customer, the full list otherwise.
   * @returns Resolves once the read has settled.
   */
  const load = async (): Promise<void> => {
    if (isCustomer.value) {
      await accountsStore.loadMine()
      return
    }
    await accountsStore.load()
  }

  return { fixedAccountId, accounts, ownerName, load }
}
