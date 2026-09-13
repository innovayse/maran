import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useAuditApi } from '../composables/apis/useAuditApi'
import { ApiError } from '../composables/useApi'
import type { AuditEvent } from '../types/audit'

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

  /** True while the request is in flight. */
  const loading: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the most recent failed load, or `null` when the last load
   * succeeded or has not been attempted yet. Rendered verbatim — the SPA owns no error text.
   */
  const errorMessage: Ref<string | null> = ref(null)

  /** True once the journal has been loaded at least once, successfully. */
  const isLoaded: Ref<boolean> = ref(false)

  /**
   * Loads the journal, replacing what is held.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loading.value = true
    try {
      events.value = await api.list()
      errorMessage.value = null
      isLoaded.value = true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
    } finally {
      loading.value = false
    }
  }

  return { events, loading, errorMessage, isLoaded, load }
})
