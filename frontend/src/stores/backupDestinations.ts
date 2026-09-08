import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useBackupDestinationsApi } from '../composables/apis/useBackupDestinationsApi'
import { ApiError } from '../composables/useApi'
import type { BackupDestination } from '../types/backupDestination'

/**
 * Owns the list of places this server records backups as living. The destinations page reads state
 * from here and calls its actions — it never touches the API layer (rules/vue.md: "API composables
 * are called from Pinia stores ONLY").
 *
 * **An empty list is a success, not a failure, and the screen says which.** A panel whose startup
 * reconciliation has not run yet answers `200 []`; that is a real answer about the server and is
 * held apart from a failure in {@link isLoaded} plus an empty {@link destinations}, so the screen
 * never renders "nothing configured" over a request that actually broke.
 *
 * **Nothing here holds a credential.** The endpoint carries none — no destination on this build has
 * one — and the shape the store keeps is `BackupDestination`, which declares no secret member. A
 * store that kept the raw payload would be the place a future credential silently arrived and sat
 * in memory for a screen to print.
 */
export const useBackupDestinationsStore = defineStore('backupDestinations', () => {
  const api = useBackupDestinationsApi()

  /** The destinations the panel reports, in the order it returned them: the default one first. */
  const destinations: Ref<BackupDestination[]> = ref([])

  /** True once a read has settled, successfully or not. */
  const isLoaded: Ref<boolean> = ref(false)

  /** True while a read is in flight. */
  const loading: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the last failed read, or `null`.
   *
   * Rendered verbatim. A customer who reaches this screen is answered 403 and reads the panel's own
   * sentence here; the SPA holds no copy of that text (rules/vue.md — the backend owns it).
   */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Reads every destination, replacing whatever was held.
   *
   * The held list is cleared BEFORE the request rather than after it: a failed reload that left the
   * previous answer on screen would show a destination list beside an error saying the list could
   * not be read.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loading.value = true
    isLoaded.value = false
    destinations.value = []
    errorMessage.value = null
    try {
      destinations.value = await api.list()
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
    } finally {
      isLoaded.value = true
      loading.value = false
    }
  }

  return {
    destinations,
    isLoaded,
    loading,
    errorMessage,
    load,
  }
})
