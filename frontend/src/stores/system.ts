import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useSystemApi } from '../composables/apis/useSystemApi'
import { ApiError } from '../composables/useApi'
import type { LoadingState } from '../types/loading'

/**
 * Pinia setup store owning system health status. Calls {@link useSystemApi}
 * to fetch `/health` and exposes its state to views; views read this store
 * and call `checkHealth` rather than touching the API composable directly
 * (rules/vue.md).
 *
 * Error text is never generated here: when the backend responds with an
 * error, its already-localized `title`/`detail` is stored verbatim in
 * `errorMessage` (rules/vue.md: "the backend owns their text"). Only when
 * the request never reaches the backend at all is there no message, and the
 * view falls back to the one frontend-owned "unreachable" string.
 */
export const useSystemStore = defineStore('system', () => {
  const api = useSystemApi()

  /**
   * Health status reported by the backend, or `null` before the first
   * successful check.
   */
  const status: Ref<string | null> = ref(null)

  /**
   * Backend-localized error message from the most recent failed check, or
   * `null` if the last check succeeded or never reached the server.
   */
  const errorMessage: Ref<string | null> = ref(null)

  /** Where the health check stands; every failure sets `error`, whatever its cause. */
  const healthState: Ref<LoadingState> = ref('loading')

  /**
   * Fetches `/health` and updates `status`, `errorMessage` and `healthState`
   * from the result.
   * @returns A promise that resolves once the check has settled.
   */
  const checkHealth = async (): Promise<void> => {
    healthState.value = 'loading'
    try {
      const health = await api.getHealth()
      status.value = health.status
      errorMessage.value = null
      healthState.value = 'loaded'
    } catch (error) {
      status.value = null
      // A response with an error carries the backend's own already-localized text; a request that
      // never reached the backend (network/DNS failure, CORS) carries none.
      errorMessage.value = error instanceof ApiError ? error.message : null
      healthState.value = 'error'
    }
  }

  return { status, errorMessage, healthState, checkHealth }
})
