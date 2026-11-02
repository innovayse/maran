import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useAccountMembershipsApi } from '../composables/apis/useAccountMembershipsApi'
import { ApiError } from '../composables/useApi'
import type { AccountMembership } from '../types/accountMembership'
import type { LoadingState } from '../types/loading'

/**
 * Owns the hosting accounts the signed-in person may act for, and the switch between them. The
 * server keeps the memberships; this store keeps the last list it was told. Components never touch
 * the API composable (rules/vue.md).
 *
 * **Reading is advice, the server is the boundary.** The list only decides whether the header draws
 * a selector; every tenant request is scoped by the account claim of the access token, which the
 * server issues, so a list this store got wrong could mislead the page but never widen what a
 * request can reach.
 */
export const useAccountMembershipsStore = defineStore('accountMemberships', () => {
  const api = useAccountMembershipsApi()

  /** The accounts as last reported, ordered by name. */
  const items: Ref<AccountMembership[]> = ref([])

  /** Where the last read stands. */
  const loadState: Ref<LoadingState> = ref('loading')

  /** Backend-localized message from the last refused read or switch, rendered verbatim. */
  const errorMessage: Ref<string | null> = ref(null)

  /** True while a switch is on its way; the selector ignores presses until it settles. */
  const switching: Ref<boolean> = ref(false)

  /** The account the sign-in is acting for, or null before the first answer or when it has none. */
  const active: ComputedRef<AccountMembership | null> = computed(() => {
    return (
      items.value.find((item): boolean => {
        return item.isCurrent
      }) ?? null
    )
  })

  /** Whether the person holds more than one account, which is the only time a selector is worth drawing. */
  const hasChoice: ComputedRef<boolean> = computed(() => {
    return items.value.length >= 2
  })

  /**
   * Reads the accounts the caller may switch between.
   *
   * Failures are captured into `errorMessage` rather than thrown: the shell renders without a
   * selector, and a list that cannot be read is a display state.
   * @returns Resolves once the request settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loadState.value = 'loading'
    errorMessage.value = null
    try {
      items.value = await api.list()
      loadState.value = 'loaded'
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      loadState.value = 'error'
    }
  }

  /**
   * Makes the sign-in act for another account.
   *
   * Resolves true only when the server accepted the switch. The caller then reloads the panel: the
   * token the server answers with is deliberately not adopted here, because the reload restores the
   * session from its refresh cookie, which carries the same choice (see `ShellAccountSelector`).
   * @param accountId The account to act for.
   * @returns True when the switch was made; false when the panel refused it (the reason is in `errorMessage`).
   */
  const switchTo = async (accountId: string): Promise<boolean> => {
    if (switching.value) {
      return false
    }
    switching.value = true
    errorMessage.value = null
    try {
      await api.switchTo({ accountId })
      return true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      switching.value = false
    }
  }

  /**
   * Forgets the last failure's message, once the person has been told.
   * @returns Nothing.
   */
  const dismissError = (): void => {
    errorMessage.value = null
  }

  /**
   * Forgets the held list, for sign-out.
   * @returns Nothing.
   */
  const clear = (): void => {
    items.value = []
    loadState.value = 'loading'
    errorMessage.value = null
  }

  return { items, loadState, errorMessage, switching, active, hasChoice, load, switchTo, dismissError, clear }
})
