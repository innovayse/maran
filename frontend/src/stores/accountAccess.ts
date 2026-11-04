import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useProfileApi } from '../composables/apis/useProfileApi'
import type { HeldAccountAccess } from '../types/profile'

/**
 * Owns the caller's standing on the account they act for (its owner, or a member holding a set of
 * features), which the navigation reads to offer a member only what the owner granted.
 *
 * It reads the profile itself and deliberately does NOT share `stores/profile.ts`: that store drops
 * what it holds before each read so the profile screen shows a loading state, and the shell's read
 * would make the open profile screen flash on every account switch.
 *
 * **A failed read is "not reported", never a refusal.** The menu is advice that errs permissive
 * (rules/security.md): the endpoints refuse a member regardless, so a profile that cannot be read
 * leaves every item offered rather than hiding the product behind an error.
 */
export const useAccountAccessStore = defineStore('accountAccess', () => {
  const api = useProfileApi()

  /**
   * The standing as last answered, with the account it was read for, or `null` before the first
   * answer and after sign-out. A read on its way leaves the previous answer in place, so the menu
   * keeps the grants it was drawn from until the new account's have arrived; a failed read replaces
   * it with an answer holding no standing.
   */
  const held: Ref<HeldAccountAccess | null> = ref(null)

  /** The in-flight read, so a newer read or {@link clear} abandons it instead of applying a stale answer. */
  const pending: Ref<AbortController | null> = ref(null)

  /**
   * Reads the caller's standing for one account. A newer read supersedes an older one still on its
   * way, so the answer kept is always the latest account's.
   * @param accountId The account the caller is acting for.
   * @returns Resolves once the read settled, successfully or not.
   */
  const load = async (accountId: string): Promise<void> => {
    pending.value?.abort()
    const controller = new AbortController()
    pending.value = controller
    try {
      const profile = await api.get(controller.signal)
      if (!controller.signal.aborted) {
        held.value = { accountId, access: profile.accountAccess }
      }
    } catch {
      // Advice only: a failed read leaves the standing unreported. It is still recorded against its
      // account, so the navigation sees the read for that account as settled and falls back to the
      // permissive menu instead of waiting for grants that will not come. An aborted one is a newer
      // read or a sign-out, and must not overwrite what that one decided.
      if (!controller.signal.aborted) {
        held.value = { accountId, access: null }
      }
    }
  }

  /**
   * Forgets the standing and abandons any read in flight, so the next sign-in does not begin with
   * the previous person's grants.
   * @returns Nothing.
   */
  const clear = (): void => {
    pending.value?.abort()
    pending.value = null
    held.value = null
  }

  return { held, load, clear }
})
