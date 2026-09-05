import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useAuthApi } from '../composables/apis/useAuthApi'
import { ApiError } from '../composables/useApi'
import type {
  AuthenticatedUser,
  CompleteSetupRequest,
  LoginRequest,
  AuthenticatedSession,
  RecoveryCodes,
  ResetPasswordRequest,
  Session,
  TotpEnrolment,
} from '../types/auth'

/**
 * Owns who is signed in. Every screen reads state from here and calls its actions;
 * none of them touches the API composable directly (rules/vue.md).
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

  /** Backend-localized message from the last failed action, rendered verbatim. */
  const errorMessage: Ref<string | null> = ref(null)

  /** The username whose sign-in is waiting for a second factor, or `null`. */
  const twoFactorUsername: Ref<string | null> = ref(null)

  /** Whether the panel already has an administrator; `null` until asked. */
  const isSetupComplete: Ref<boolean | null> = ref(null)

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

  /** The caller's live sessions, as last loaded. */
  const sessions: Ref<Session[]> = ref([])

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
   * @returns Nothing; state is cleared synchronously.
   */
  const clear = (): void => {
    accessToken.value = null
    user.value = null
    sessions.value = []
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
        // Refresh answers with the signed-in half directly and has no "a factor is
        // owed" case: the cookie either still stands or the call failed. Wrapping it
        // in a nullable envelope would re-create a state that cannot occur.
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
      const result = await api.verifyTwoFactor({ username: twoFactorUsername.value, password, code })
      accept(result.session)
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

  /**
   * Asks whether the panel already has an administrator, once per page load.
   * @returns Resolves once the answer is known.
   */
  const loadSetupState = async (): Promise<void> => {
    if (isSetupComplete.value !== null) {
      return
    }

    try {
      isSetupComplete.value = (await api.setupState()).isComplete
    } catch {
      // An unreachable panel is not an un-set-up one. Assuming "complete" keeps a
      // visitor on the login screen rather than offering to claim a server that
      // may well already have an owner.
      isSetupComplete.value = true
    }
  }

  /**
   * Creates the panel's first administrator.
   * @param request The token and the administrator's details.
   * @returns True when the administrator was created.
   */
  const completeSetup = async (request: CompleteSetupRequest): Promise<boolean> => {
    loading.value = true
    errorMessage.value = null
    try {
      await api.completeSetup(request)
      isSetupComplete.value = true
      return true
    } catch (error) {
      remember(error)
      return false
    } finally {
      loading.value = false
    }
  }

  /**
   * Loads the caller's live sessions.
   * @returns Resolves once the request has settled.
   */
  const loadSessions = async (): Promise<void> => {
    loading.value = true
    errorMessage.value = null
    try {
      sessions.value = await api.listSessions()
    } catch (error) {
      remember(error)
    } finally {
      loading.value = false
    }
  }

  /**
   * Ends one of the caller's sessions and drops it from the held list.
   * @param id The session to end.
   * @returns True when it was ended.
   */
  const revokeSession = async (id: string): Promise<boolean> => {
    errorMessage.value = null
    try {
      await api.revokeSession(id)
      sessions.value = sessions.value.filter((session) => {
        return session.id !== id
      })
      return true
    } catch (error) {
      remember(error)
      return false
    }
  }

  /**
   * Starts a two-factor enrolment. Nothing is enabled until it is confirmed.
   * @returns The secret and its provisioning URI, or `null` when the request failed.
   */
  const beginTwoFactorEnrolment = async (): Promise<TotpEnrolment | null> => {
    errorMessage.value = null
    try {
      return await api.beginTwoFactorEnrolment()
    } catch (error) {
      remember(error)
      return null
    }
  }

  /**
   * Completes an enrolment.
   * @param secret The secret handed out by the enrolment step.
   * @param code A code the user's app produced from it.
   * @returns The recovery codes, shown once, or `null` when the code was refused.
   */
  const confirmTwoFactorEnrolment = async (secret: string, code: string): Promise<RecoveryCodes | null> => {
    errorMessage.value = null
    try {
      return await api.confirmTwoFactorEnrolment(secret, code)
    } catch (error) {
      remember(error)
      return null
    }
  }

  /**
   * Turns the second factor off.
   * @param code A current code or a recovery code.
   * @returns True when it was turned off.
   */
  const disableTwoFactor = async (code: string): Promise<boolean> => {
    errorMessage.value = null
    try {
      await api.disableTwoFactor(code)
      return true
    } catch (error) {
      remember(error)
      return false
    }
  }

  /**
   * Asks the panel to send a password-reset link.
   *
   * **Reports nothing about the address, and that is the whole point.** The backend
   * answers identically for an address that has an account and one that does not,
   * so this action returns `void` rather than a boolean: a caller with a boolean
   * would eventually branch on it, and a screen that renders two outcomes is the
   * account-enumeration oracle the backend spent effort closing. A transport
   * failure is remembered as an error because it says nothing about the address —
   * it says the panel could not be reached.
   * @param email The address to send the link to.
   * @returns Resolves once the request has settled, whatever it settled as.
   */
  const requestPasswordReset = async (email: string): Promise<void> => {
    loading.value = true
    errorMessage.value = null
    try {
      await api.requestPasswordReset(email)
    } catch (error) {
      remember(error)
    } finally {
      loading.value = false
    }
  }

  /**
   * Sets a new password from a reset link.
   * @param request The token from the mail and the new password.
   * @returns True when the password was changed; false when the panel refused.
   */
  const resetPassword = async (request: ResetPasswordRequest): Promise<boolean> => {
    loading.value = true
    errorMessage.value = null
    try {
      await api.resetPassword(request)
      return true
    } catch (error) {
      // The backend gives one refusal for a token that never existed, one that has
      // expired and one already spent. It is rendered as sent, and nothing here
      // inspects it: any branch on the reason would re-create the distinction the
      // single message exists to hide.
      remember(error)
      return false
    } finally {
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
    isSetupComplete,
    isRestored,
    sessions,
    isAuthenticated,
    renewAccessToken,
    restore,
    login,
    verifyTwoFactor,
    logout,
    logoutEverywhere,
    loadSetupState,
    completeSetup,
    loadSessions,
    revokeSession,
    beginTwoFactorEnrolment,
    confirmTwoFactorEnrolment,
    disableTwoFactor,
    requestPasswordReset,
    resetPassword,
  }
})
