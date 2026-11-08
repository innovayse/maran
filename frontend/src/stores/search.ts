import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useSearchApi } from '../composables/apis/useSearchApi'
import type { SearchGroup } from '../types/search'

/**
 * Owns the server-side half of the jump-to palette: the groups the panel last answered with for the
 * term typed. The term is kept out of every URL, the console and storage; this store holds only the
 * answer, and clears it when the palette closes.
 *
 * **A failed read is a muted state, not an error.** The palette still offers the pages it can find
 * without the server, so a failure here leaves `failed` set and no groups.
 */
export const useSearchStore = defineStore('search', () => {
  const api = useSearchApi()

  /** The groups the panel answered with for the latest term; empty before an answer and after {@link clear}. */
  const groups: Ref<SearchGroup[]> = ref([])

  /** Whether a request is in flight. */
  const loading: Ref<boolean> = ref(false)

  /** Whether the latest request failed as a whole. */
  const failed: Ref<boolean> = ref(false)

  /** The in-flight request, so a newer keystroke abandons it instead of applying a stale answer. */
  const pending: Ref<AbortController | null> = ref(null)

  /**
   * Asks the panel for the term, abandoning any earlier request.
   * @param term The trimmed text typed, at least the server's minimum length.
   * @returns Resolves once the request settled, successfully or not.
   */
  const search = async (term: string): Promise<void> => {
    pending.value?.abort()
    const controller = new AbortController()
    pending.value = controller
    loading.value = true
    failed.value = false
    try {
      const result = await api.search(term, controller.signal)
      if (!controller.signal.aborted) {
        groups.value = result.groups
      }
    } catch {
      // The palette's own pages still work; a failed server search is shown as a muted note.
      if (!controller.signal.aborted) {
        groups.value = []
        failed.value = true
      }
    } finally {
      if (pending.value === controller) {
        pending.value = null
        loading.value = false
      }
    }
  }

  /**
   * Drops the answer and any request in flight, for a term too short to search and for a closed palette.
   * @returns Nothing.
   */
  const clear = (): void => {
    pending.value?.abort()
    pending.value = null
    groups.value = []
    loading.value = false
    failed.value = false
  }

  return { groups, loading, failed, search, clear }
})
