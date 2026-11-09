import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { FolderListing } from '../components/files/FileFolderPicker.vue'
import { useFileOperationsStore } from '../stores/fileOperations'

/** What {@link useFolderPicker} reads from its host; the host's props satisfy it. */
export interface FolderPickerSource {
  /** The account whose files are listed. */
  readonly accountId: string
}

/** What {@link useFolderPicker} hands the dialog host. */
export interface FolderPicker {
  /** What the picker shows: the folders read so far and how the read stands. */
  listing: ComputedRef<FolderListing>
  /**
   * Starts a fresh walk in a folder, as when a dialog that picks a destination opens.
   * @param path The folder to list, relative to the home.
   * @returns Resolves once the first page has settled.
   */
  browse: (path: string) => Promise<void>
  /**
   * Narrows the folder being looked at to the names the panel finds for a term.
   * @param term The typed text; empty clears the search.
   * @returns Resolves once the first page of matches has settled.
   */
  find: (term: string) => Promise<void>
  /**
   * Appends the next page of the listing, if the panel holds one.
   * @returns Resolves once the page has settled.
   */
  more: () => Promise<void>
}

/**
 * Drives the destination picker of the move, copy and extract dialogs: which folder is being looked
 * at, the search typed in it, and the paged listing the operations store keeps. The picker never
 * holds more than the pages the person asked for; the panel searches and pages (rules/vue.md,
 * Data comes from the backend).
 * @param source The host's props; the account is read lazily, so a changing prop is followed.
 * @returns The listing and the three ways to change it.
 */
export const useFolderPicker = (source: FolderPickerSource): FolderPicker => {
  const { t } = useI18n()
  const store = useFileOperationsStore()

  /** The folder being looked at, relative to the home. */
  const path: Ref<string> = ref('')

  /** The name search typed in that folder; empty for none. */
  const search: Ref<string> = ref('')

  /** What the picker shows. */
  const listing: ComputedRef<FolderListing> = computed(() => {
    const folders = store.folders
    return {
      folders: folders.state === 'loaded' ? folders.value : [],
      loading: folders.state === 'loading',
      loadingMore: store.foldersLoadingMore,
      hasMore: store.foldersHasMore,
      errorMessage: folders.state === 'failed' ? (folders.message ?? t('common.list.unreachable')) : null,
    }
  })

  /**
   * Lists a folder from its first page, with no search.
   * @param next The folder to list.
   * @returns Resolves once the read settled.
   */
  const browse = async (next: string): Promise<void> => {
    path.value = next
    search.value = ''
    await store.loadFolders(source.accountId, next, '')
  }

  /**
   * Lists the folder being looked at again, narrowed by a search.
   * @param term The typed text.
   * @returns Resolves once the read settled.
   */
  const find = async (term: string): Promise<void> => {
    search.value = term
    await store.loadFolders(source.accountId, path.value, term)
  }

  /**
   * Reads the next page of the current listing.
   * @returns Resolves once the read settled.
   */
  const more = async (): Promise<void> => {
    await store.loadMoreFolders(source.accountId, path.value, search.value)
  }

  return { listing, browse, find, more }
}
