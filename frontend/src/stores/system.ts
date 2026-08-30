import { defineStore } from 'pinia'
import { ref, type Ref  } from 'vue'
import { useSystemApi } from '../composables/apis/useSystemApi'
import { ApiError } from '../composables/useApi'

/**
 * Pinia setup store owning system health status. Calls {@link useSystemApi}
 * to fetch `/health` and exposes loading/error/status state to views; views
 * read this store and call `checkHealth` rather than touching the API
 * composable directly (rules/vue.md).
 *
 * Error text is never generated here: when the backend responds with an
 * error, its already-localized `title`/`detail` is stored verbatim in
 * `errorMessage` (rules/vue.md: "the backend owns their text"). Only when
 * the request never reaches the backend at all (`unreachable`) does the
 * view fall back to a frontend-owned string, because there is no server
 * message to show in that case.
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
  /**
   * Whether the most recent check failed before reaching the backend at
   * all (e.g. network/DNS failure), so no server-provided message exists.
   */
  const unreachable: Ref<boolean> = ref(false)

  /**
   * Fetches `/health` and updates `status`/`errorMessage`/`unreachable`
   * from the result.
   * @returns A promise that resolves once the check has settled.
   */
  const checkHealth = async (): Promise<void> => {
    try {
      const health = await api.getHealth()
      status.value = health.status
      errorMessage.value = null
      unreachable.value = false
    } catch (error) {
      status.value = null
      if (error instanceof ApiError) {
        // The backend responded, just with an error: render its own
        // already-localized text rather than inventing frontend copy.
        errorMessage.value = error.message
        unreachable.value = false
      } else {
        // The request never reached the backend (network/DNS failure, CORS,
        // etc.) — there is no server message to show, so fall back to the
        // one frontend-owned string for this case.
        errorMessage.value = null
        unreachable.value = true
      }
    }
  }

  return { status, errorMessage, unreachable, checkHealth }
})
