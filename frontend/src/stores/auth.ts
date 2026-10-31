import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useAuthApi } from '../composables/apis/useAuthApi'
import { ApiError } from '../composables/useApi'
import type { AuthenticatedSession, AuthenticatedUser, LoginRequest } from '../types/auth'

/**
 * Owns who is signed in: the access token, the user it belongs to, and the actions that obtain,
 * renew and end it. Every screen reads state from here and calls its actions; none of them touches
 * the API composable directly (rules/vue.md). The other account flows have stores of their own —
 * first-run setup (`setup.ts`), the second factor's enrolment (`twoFactor.ts`), the signed-in
 * devices (`sessions.ts`) and the e-mailed password links (`passwordLinks.ts`).
 *
 * The access token lives in this store's `ref` and nowhere else — not
 * `localStorage`, not a readable cookie. A token in `localStorage` is readable by
 * any successful XSS and survives the tab; one in a closure is not persisted
 * anywhere an attacker can reach without already running in the page. The cost is
 * one refresh per page load, which is what the httpOnly refresh cookie exists for.
 */
export const useAuthStore = defineStore('auth', () => {
  const api = useAuthApi()

  /** The signed access token, or `null` when nobody is signed in. */
  const accessToken: Ref<string | null> = ref(null)

  /** Who is signed in, or `null`. */
  const user: Ref<AuthenticatedUser | null> = ref(null)

  /** True while a sign-in, refresh or sign-out is in flight. */
  const loading: Ref<boolean> = ref(false)

  /** Backend-localized message from the last failed sign-in or sign-out, rendered verbatim. */
  const errorMessage: Ref<string | null> = ref(null)

  /** The username whose sign-in is waiting for a second factor, or `null`. */
  const twoFactorUsername: Ref<string | null> = ref(null)

  /**
   * True when the panel forces administrators to hold a second factor and the
   * signed-in one does not yet.
   *
   * The sign-in succeeded and there IS a token, so `isAuthenticated` is true — but
   * that token reaches only the enrolment endpoints, and every other one answers
   * 403. The router reads this to keep the person inside enrolment; without it the
   * panel would render its whole shell over screens that can only fail.
   */
  const requiresTwoFactorSetup: Ref<boolean> = ref(false)

  /** True once the store has tried to restore a session, successfully or not. */
  const isRestored: Ref<boolean> = ref(false)

  /**
   * The renewal currently in flight, shared by every caller.
   *
   * Without this, ten parallel calls after a page reload each rotate the refresh
   * token, and nine of them present a token a sibling has already spent — which
   * the backend correctly treats as reuse and answers by revoking the whole
   * session family. One promise means one rotation.
   */
  const renewal: Ref<Promise<boolean> | null> = ref(null)

  /** True when the store holds a usable access token. */
  const isAuthenticated: ComputedRef<boolean> = computed(() => {
    return accessToken.value !== null
  })

  /**
   * Stores the outcome of a successful sign-in or refresh.
   *
   * Takes the whole result rather than its parts: the token, its user and the
   * enrolment flag arrive together or not at all, so there is no caller who could
   * pass two of the three and leave the shell open to a token every endpoint but
   * enrolment refuses.
   * @param session The signed-in half, or `null` when a second factor is still owed.
   * @returns Nothing; state is updated synchronously.
   */
  const accept = (session: AuthenticatedSession | null): void => {
    accessToken.value = session?.accessToken ?? null
    user.value = session?.user ?? null
    requiresTwoFactorSetup.value = session?.requiresTwoFactorSetup ?? false
  }

  /**
   * Forgets everything about the signed-in user.
   *
   * What other stores hold for that user (the module catalogue, the signed-in devices) is cleared
   * by the root component when it sees `isAuthenticated` turn false, not from here: a store never
   * reaches into another (rules/vue.md "State").
   * @returns Nothing; state is cleared synchronously.
   */
  const clear = (): void => {
    accessToken.value = null
    user.value = null
    twoFactorUsername.value = null
    requiresTwoFactorSetup.value = false
  }

  /**
   * Reads a failure's backend-localized text, or clears the message for anything
   * that is not an API error.
   * @param error The caught error.
   * @returns Nothing; `errorMessage` is updated.
   */
  const remember = (error: unknown): void => {
    errorMessage.value = error instanceof ApiError ? error.message : null
  }

  /**
   * Forgets the last failure's message. A message belongs to the screen that produced it, so the
   * root component calls this on every navigation between screens (`App.vue`).
   * @returns Nothing; `errorMessage` is cleared synchronously.
   */
  const dismissError = (): void => {
    errorMessage.value = null
  }

  /**
   * Renews the access token from the refresh cookie, sharing one request across
   * concurrent callers.
   * @returns True when a new token was obtained.
   */
  const renewAccessToken = async (): Promise<boolean> => {
    if (renewal.value !== null) {
      return renewal.value
    }

    const attempt = api
      .refresh()
      .then((session) => {
        // `null` is the backend's answer to a browser that sent no refresh cookie:
        // nobody is signed in here, which is a state rather than a failure, so it is
        // cleared exactly like a refused cookie but without an error on the wire.
        if (session === null) {
          clear()
          return false
        }

        accept(session)
        return true
      })
      .catch(() => {
        clear()
        return false
      })
      .finally(() => {
        renewal.value = null
      })

    renewal.value = attempt
    return attempt
  }

  /**
   * Restores a session on page load, if the refresh cookie is still good.
   * @returns Resolves once the attempt has settled.
   */
  const restore = async (): Promise<void> => {
    if (isRestored.value) {
      return
    }

    await renewAccessToken()
    isRestored.value = true
  }

  /**
   * Signs in with a username and password.
   * @param request The credentials.
   * @returns True when the user is fully signed in; false when a second factor is owed or the attempt failed.
   */
  const login = async (request: LoginRequest): Promise<boolean> => {
    loading.value = true
    errorMessage.value = null
    try {
      const result = await api.login(request)
      if (result.session === null) {
        twoFactorUsername.value = request.username
        return false
      }

      accept(result.session)
      return true
    } catch (error) {
      remember(error)
      return false
    } finally {
      loading.value = false
    }
  }

  /**
   * Completes a sign-in that stopped for a second factor.
   * @param password The password from the first step, which the backend checks again.
   * @param code A code from the authenticator app, or a recovery code.
   * @returns True when the user is now signed in.
   */
  const verifyTwoFactor = async (password: string, code: string): Promise<boolean> => {
    if (twoFactorUsername.value === null) {
      return false
    }

    loading.value = true
    errorMessage.value = null
    try {
      const session = await api.verifyTwoFactor({ username: twoFactorUsername.value, password, code })
      accept(session)
      twoFactorUsername.value = null
      return true
    } catch (error) {
      remember(error)
      return false
    } finally {
      loading.value = false
    }
  }

  /**
   * Signs out of this device.
   * @returns Resolves once the request has settled; state is cleared either way.
   */
  const logout = async (): Promise<void> => {
    loading.value = true
    try {
      await api.logout()
    } catch (error) {
      // A sign-out that the server refused still ends the session here: leaving
      // the user apparently signed in because the network failed is the worse
      // of the two wrong answers.
      remember(error)
    } finally {
      clear()
      loading.value = false
    }
  }

  /**
   * Signs out of every device, including this one.
   * @returns Resolves once the request has settled.
   */
  const logoutEverywhere = async (): Promise<void> => {
    loading.value = true
    try {
      await api.logoutEverywhere()
    } catch (error) {
      remember(error)
    } finally {
      clear()
      loading.value = false
    }
  }

  return {
    accessToken,
    user,
    loading,
    errorMessage,
    twoFactorUsername,
    requiresTwoFactorSetup,
    isRestored,
    isAuthenticated,
    renewAccessToken,
    restore,
    login,
    verifyTwoFactor,
    logout,
    dismissError,
    logoutEverywhere,
  }
})
