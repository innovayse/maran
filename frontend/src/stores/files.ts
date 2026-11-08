import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useFilesApi } from '../composables/apis/useFilesApi'
import { ApiError } from '../composables/useApi'
import type { FileEntry } from '../types/file'
import type { ListLoad, ListRequest } from '../types/list'

/**
 * Owns the file browser's state: the page of the folder last answered, and which of its rows are
 * ticked. The folder, the account, the hidden toggle, the search, the sort and the page all live in
 * the URL (`useServerTable`); this store only reads one page for them and holds the answer, so the
 * page cannot disagree with the address bar.
 *
 * Error text is never generated here: when the panel refuses a read, its already-localized
 * `title`/`detail` is handed back verbatim for the list to show (rules/vue.md).
 *
 * The mutating actions (rename, move, trash and the rest) live in their own store; the page asks the
 * list to read again after one of them, so this store imports no other store.
 */
export const useFilesStore = defineStore('files', () => {
  const api = useFilesApi()

  /** The rows of the folder page last answered by the panel; empty before the first answer. */
  const entries: Ref<FileEntry[]> = ref([])

  /** The paths of the ticked rows, in the order they were ticked. Only rows of the current page. */
  const selectedPaths: Ref<string[]> = ref([])

  /** The ticked rows themselves, in the order the page lists them. */
  const selectedEntries: ComputedRef<FileEntry[]> = computed(() => {
    return entries.value.filter((entry) => {return selectedPaths.value.includes(entry.path)})
  })

  /** Whether every row of the page is ticked; false for an empty page. */
  const allSelected: ComputedRef<boolean> = computed(() => {
    return entries.value.length > 0 && selectedEntries.value.length === entries.value.length
  })

  /**
   * Reads one page of a folder into {@link entries} and reports how the read ended, for the file
   * browser's `useServerTable`.
   *
   * A selection never outlives the page it was made on: after a read the ticks that name a row no
   * longer on the page are dropped, so a bulk action can never act on a row the person cannot see.
   * @param accountId The account whose files are listed.
   * @param request The page, size, search and sort; its filters carry the folder (`path`) and the
   * hidden toggle (`hidden`, `1` when names beginning with a dot are shown).
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (accountId: string, request: ListRequest): Promise<ListLoad<FileEntry>> => {
    try {
      const page = await api.list({
        accountId,
        path: request.filters.path ?? '',
        showHidden: request.filters.hidden === '1',
        list: { page: request.page, pageSize: request.pageSize, search: request.search, sort: request.sort },
      })
      entries.value = page.items
      selectedPaths.value = selectedPaths.value.filter((path) => {
        return page.items.some((entry) => {return entry.path === path})
      })
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Ticks or unticks one row.
   * @param path The row's path.
   * @param selected Whether the row should be ticked.
   * @returns Nothing.
   */
  const select = (path: string, selected: boolean): void => {
    const without = selectedPaths.value.filter((held) => {return held !== path})
    selectedPaths.value = selected ? [...without, path] : without
  }

  /**
   * Ticks every row of the page, or clears every tick.
   * @param selected Whether the rows should be ticked.
   * @returns Nothing.
   */
  const selectAll = (selected: boolean): void => {
    selectedPaths.value = selected ? entries.value.map((entry) => {return entry.path}) : []
  }

  /**
   * Clears every tick, as when the bulk bar's selection is dismissed or an action finished.
   * @returns Nothing.
   */
  const clearSelection = (): void => {
    selectedPaths.value = []
  }

  /**
   * Forgets the page and the ticks, as when the person switches to another account: one account's
   * rows must never show under another's name while the new answer is on its way.
   * @returns Nothing.
   */
  const reset = (): void => {
    entries.value = []
    selectedPaths.value = []
  }

  return { entries, selectedPaths, selectedEntries, allSelected, list, select, selectAll, clearSelection, reset }
})
