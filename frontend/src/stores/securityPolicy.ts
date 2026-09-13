import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useSecurityPolicyApi } from '../composables/apis/useSecurityPolicyApi'
import { ApiError } from '../composables/useApi'
import type { SaveSecurityPolicyRequest, SecurityPolicy } from '../types/securityPolicy'

/**
 * Owns the panel's security policy for the settings screen. The screen reads state
 * from here and calls its actions; it never touches the API composable
 * (rules/vue.md).
 *
 * There is no default policy invented here. A panel that has not answered yet holds
 * `null`, and the form renders nothing rather than plausible-looking numbers: a
 * suggested minimum length on screen is indistinguishable from one somebody chose,
 * and this SPA does not invent domain data.
 */
export const useSecurityPolicyStore = defineStore('securityPolicy', () => {
  const api = useSecurityPolicyApi()

  /** The policy the panel is enforcing, or `null` until it has been read. */
  const policy: Ref<SecurityPolicy | null> = ref(null)

  /** True while a read or a save is in flight. */
  const loading: Ref<boolean> = ref(false)

  /** True once the last save succeeded, so the screen can confirm it. */
  const saved: Ref<boolean> = ref(false)

  /** Backend-localized message from the last failure, rendered verbatim. */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Reads a failure's backend-localized text, or clears it for anything that is not
   * an API error.
   * @param error The caught error.
   * @returns Nothing; `errorMessage` is updated.
   */
  const remember = (error: unknown): void => {
    errorMessage.value = error instanceof ApiError ? error.message : null
  }

  /**
   * Loads the policy the panel is enforcing.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loading.value = true
    errorMessage.value = null
    try {
      policy.value = await api.get()
    } catch (error) {
      remember(error)
    } finally {
      loading.value = false
    }
  }

  /**
   * Replaces the policy and keeps what was stored as the new held state.
   * @param request The complete policy to store.
   * @returns True when it was stored.
   */
  const save = async (request: SaveSecurityPolicyRequest): Promise<boolean> => {
    loading.value = true
    errorMessage.value = null
    saved.value = false
    try {
      await api.save(request)
      // Held rather than re-read: the endpoint replaces the whole document, so what
      // was sent IS what is stored, and a second round trip would only add a window
      // in which the screen shows the old values.
      policy.value = { ...request }
      saved.value = true
      return true
    } catch (error) {
      remember(error)
      return false
    } finally {
      loading.value = false
    }
  }

  return { policy, loading, saved, errorMessage, load, save }
})
