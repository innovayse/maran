import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useSecurityPolicyApi } from '../composables/apis/useSecurityPolicyApi'
import { ApiError } from '../composables/useApi'
import type { LoadingState, Read, Submission } from '../types/loading'
import type { SaveSecurityPolicyRequest, SecurityPolicy } from '../types/securityPolicy'
import { loadingStateOf } from '../utils/loadingStateOf'

/**
 * Owns the panel's security policy for the settings screen. The screen reads state
 * from here and calls its actions; it never touches the API composable
 * (rules/vue.md).
 *
 * There is no default policy invented here. A panel that has not answered yet holds
 * no policy, and the form draws placeholders rather than plausible-looking numbers: a
 * suggested minimum length on screen is indistinguishable from one somebody chose,
 * and this SPA does not invent domain data.
 */
export const useSecurityPolicyStore = defineStore('securityPolicy', () => {
  const api = useSecurityPolicyApi()

  /**
   * The read of the policy: on its way, arrived, or refused. A save never moves it, so a refused
   * save leaves the form on screen with its alert above it.
   */
  const read: Ref<Read<SecurityPolicy>> = ref({ state: 'loading' })

  /** Where the last save stands; `done` lets the screen confirm it, `failed` carries the refusal. */
  const submission: Ref<Submission> = ref({ state: 'idle' })

  /** The policy the panel is enforcing, or `null` until it has been read. */
  const policy: ComputedRef<SecurityPolicy | null> = computed(() => {
    return read.value.state === 'loaded' ? read.value.value : null
  })

  /** Where the read stands, in the words the loading region understands. */
  const loadState: ComputedRef<LoadingState> = computed(() => {
    return loadingStateOf(read.value)
  })

  /** The panel's reason the read failed, rendered verbatim; `null` otherwise. */
  const loadMessage: ComputedRef<string | null> = computed(() => {
    return read.value.state === 'failed' ? read.value.message : null
  })

  /** The panel's reason the last save was refused, rendered verbatim; `null` otherwise. */
  const submitMessage: ComputedRef<string | null> = computed(() => {
    return submission.value.state === 'failed' ? submission.value.message : null
  })

  /** True while a read or a save is in flight. */
  const loading: ComputedRef<boolean> = computed(() => {
    return read.value.state === 'loading' || submission.value.state === 'sending'
  })

  /**
   * Reads a failure's backend-localized text, or nothing for anything that is not an API error.
   * @param error The caught error.
   * @returns The text, or `null`.
   */
  const messageOf = (error: unknown): string | null => {
    return error instanceof ApiError ? error.message : null
  }

  /**
   * Loads the policy the panel is enforcing.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    read.value = { state: 'loading' }
    try {
      read.value = { state: 'loaded', value: await api.get() }
    } catch (error) {
      read.value = { state: 'failed', message: messageOf(error) }
    }
  }

  /**
   * Replaces the policy and keeps what was stored as the new held state.
   * @param request The complete policy to store.
   * @returns True when it was stored.
   */
  const save = async (request: SaveSecurityPolicyRequest): Promise<boolean> => {
    submission.value = { state: 'sending' }
    try {
      await api.save(request)
      // Held rather than re-read: the endpoint replaces the whole document, so what
      // was sent IS what is stored, and a second round trip would only add a window
      // in which the screen shows the old values.
      read.value = { state: 'loaded', value: { ...request } }
      submission.value = { state: 'done' }
      return true
    } catch (error) {
      submission.value = { state: 'failed', message: messageOf(error) }
      return false
    }
  }

  return { policy, loading, loadState, loadMessage, submission, submitMessage, load, save }
})
