import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useViewAsApi } from '../composables/apis/useViewAsApi'
import { ApiError } from '../composables/useApi'
import type { ViewAsSession, ViewingAs } from '../types/viewAs'

/**
 * Owns what the panel says about viewing as an account or a reseller (issues #317, #314, #179): whether the current
 * token is a view-as one and of what, and the two actions that start and end viewing.
 *
 * **Nothing here decides anything.** `viewing` is only ever written from the panel's answer to
 * `GET /api/v1/view-as` or `POST /api/v1/view-as`; the store never infers it from the user's role or
 * from a flag it set itself, so a banner drawn from it says what the server holds. Swapping the access
 * token is the auth store's job: the page or the banner that starts or ends viewing calls both stores
 * in turn, because a store never reaches into another (rules/vue.md "State").
 */
export const useViewAsStore = defineStore('viewAs', () => {
  const api = useViewAsApi()

  /** The view-as session behind the current token as last reported, or `null` when there is none. */
  const viewing: Ref<ViewingAs | null> = ref(null)

  /** True while a start or an end is on its way; the controls ignore presses until it settles. */
  const acting: Ref<boolean> = ref(false)

  /** Backend-localized message from the last refused start, rendered verbatim. */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Asks the panel whether the current token is a view-as token.
   *
   * A failure reads as "not viewing": the banner is a statement about a session the server
   * confirmed, so when nothing was confirmed there is nothing to state.
   * @returns Resolves once the request settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    try {
      viewing.value = (await api.current()).viewing
    } catch {
      viewing.value = null
    }
  }

  /**
   * Runs one start request with the shared busy and refusal handling, so the two kinds of viewing
   * cannot drift apart in how they report a refusal.
   * @param request Sends the start request for one kind of subject.
   * @returns The view-as session for the auth store to adopt, or `null` when the panel refused.
   */
  const begin = async (request: () => Promise<ViewAsSession>): Promise<ViewAsSession | null> => {
    acting.value = true
    errorMessage.value = null
    try {
      const session = await request()
      viewing.value = session.viewing
      return session
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      return null
    } finally {
      acting.value = false
    }
  }

  /**
   * Starts viewing as the login that owns an account.
   * @param accountId The account.
   * @returns The view-as session for the auth store to adopt, or `null` when the panel refused.
   */
  const start = async (accountId: string): Promise<ViewAsSession | null> => {
    return begin(() => {
      return api.start({ accountId })
    })
  }

  /**
   * Starts viewing as a reseller's login. Administrators only.
   * @param resellerId The reseller.
   * @returns The view-as session for the auth store to adopt, or `null` when the panel refused.
   */
  const startReseller = async (resellerId: string): Promise<ViewAsSession | null> => {
    return begin(() => {
      return api.startReseller({ resellerId })
    })
  }

  /**
   * Ends the view-as session. The operator is no longer viewing whichever way the panel answers —
   * ended now, or already ended by expiry or by the customer — so the state is cleared either way and
   * the caller then renews to the operator's own token.
   * @returns Resolves once the request settled.
   */
  const stop = async (): Promise<void> => {
    acting.value = true
    try {
      await api.stop()
    } catch {
      // Already ended (expired, revoked, or Back pressed in another tab): nothing left to end.
    } finally {
      viewing.value = null
      acting.value = false
    }
  }

  /**
   * Forgets the view-as state, for a sign-out.
   * @returns Nothing.
   */
  const clear = (): void => {
    viewing.value = null
    errorMessage.value = null
  }

  return { viewing, acting, errorMessage, load, start, startReseller, stop, clear }
})
