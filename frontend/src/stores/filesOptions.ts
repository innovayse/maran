import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useFilesApi } from '../composables/apis/useFilesApi'
import type { FilesOptions } from '../types/file'

/**
 * Owns the limits the file manager shows: the biggest upload, edit and download, the most items
 * one batch names, and how long the trash keeps an entry. They live on the server so the SPA holds
 * no constant of its own (rules/architecture.md "The backend owns the data"); here they are only
 * displayed, and the server enforces every one whatever this store holds.
 */
export const useFilesOptionsStore = defineStore('filesOptions', () => {
  const api = useFilesApi()

  /**
   * The limits as the panel reported them, or `null` until it has answered. A screen that could not
   * read them stays permissive: the server refuses what is over a limit, the screen only advises.
   */
  const options: Ref<FilesOptions | null> = ref(null)

  /**
   * Reads the limits. A failure leaves {@link options} as it was rather than raising an error of its
   * own: the limits are advice here, and the server's refusal is what the person reads.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    try {
      options.value = await api.options()
    } catch {
      // The limits are advisory; a missing answer must not break the browser.
    }
  }

  return { options, load }
})
