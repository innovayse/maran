import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { usePrivacyApi } from '../composables/apis/usePrivacyApi'
import { ApiError } from '../composables/useApi'
import type { Submission } from '../types/loading'
import { saveDownload } from '../utils/saveDownload'

/**
 * Owns the personal-data export an administrator starts from an account's page. The page reads
 * state from here and calls its action; it never touches the API composable (rules/vue.md).
 *
 * The export is a file the browser saves, not data this store keeps: nothing the panel holds about
 * a person stays in the SPA's memory after the save.
 */
export const usePrivacyStore = defineStore('privacy', () => {
  const api = usePrivacyApi()

  /** Where the last export stands; `failed` carries the panel's refusal (a missing account, a too-large export). */
  const exportState: Ref<Submission> = ref({ state: 'idle' })

  /** The panel's reason the last export was refused, rendered verbatim; `null` otherwise. */
  const exportMessage: ComputedRef<string | null> = computed(() => {
    return exportState.value.state === 'failed' ? exportState.value.message : null
  })

  /** True while an export is being produced and downloaded. */
  const exporting: ComputedRef<boolean> = computed(() => {
    return exportState.value.state === 'sending'
  })

  /**
   * Downloads everything the panel holds about one account and hands the file to the browser.
   * @param accountId The account to export.
   * @returns True when the file was received and saved.
   */
  const exportAccount = async (accountId: string): Promise<boolean> => {
    exportState.value = { state: 'sending' }
    try {
      saveDownload(await api.exportAccount(accountId))
      exportState.value = { state: 'done' }
      return true
    } catch (error) {
      exportState.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
      return false
    }
  }

  return { exportState, exportMessage, exporting, exportAccount }
})
