import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useAuditApi } from '../composables/apis/useAuditApi'
import { ApiError } from '../composables/useApi'
import type { AuditEvent } from '../types/audit'
import type { LoadingState } from '../types/loading'

/**
 * Owns the audit journal the admin screen renders. The page reads state from here and calls
 * `load` — it never touches the API composable (rules/vue.md: "API composables are called from
 * Pinia stores ONLY").
 *
 * Nothing here mutates the journal: it is append-only, written by the backend from inside the
 * handlers that perform an action, and this store's whole surface is one read.
 */
export const useAuditStore = defineStore('audit', () => {
  const api = useAuditApi()

  /** The entries as last reported by the panel; empty before the first successful load. */
  const events: Ref<AuditEvent[]> = ref([])

  /**
   * Backend-localized message from the most recent failed load, or `null` when the last load
   * succeeded or has not been attempted yet. Rendered verbatim — the SPA owns no error text.
   */
  const errorMessage: Ref<string | null> = ref(null)

  /** Where the read of the journal stands; every failure sets `error`, whatever its cause. */
  const loadState: Ref<LoadingState> = ref('loading')

  /**
   * Loads the journal, replacing what is held.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loadState.value = 'loading'
    try {
      events.value = await api.list()
      errorMessage.value = null
      loadState.value = 'loaded'
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      loadState.value = 'error'
    }
  }

  return { events, errorMessage, loadState, load }
})
