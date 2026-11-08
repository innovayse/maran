import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAccountChoice, type AccountChoice } from './useAccountChoice'
import { useAccountFilter } from './useAccountFilter'
import type { FileOperationOrder } from './useFileOperationRunner'
import { fileBrowserFilters, useFilePathQuery, type FilePathQuery } from './useFilePathQuery'
import { useServerTable, type ServerTable } from './useServerTable'
import { useFilesStore } from '../stores/files'
import { useFileTransfersStore } from '../stores/fileTransfers'
import { useFilesOptionsStore } from '../stores/filesOptions'
import type { FileBatchAction, FileEntry, FileEntryAction } from '../types/file'
import type { ListLoad, ListRequest } from '../types/list'

/** What {@link useFileBrowser} hands the file manager page. */
export interface FileBrowser {
  /** Which account is looked at: fixed for a customer, chosen for an administrator. */
  accountChoice: AccountChoice
  /** The folder's server table: page, search, sort, state. */
  table: ServerTable
  /** Where the browser is looking: account, folder, dot-name toggle, and the actions that change them. */
  location: FilePathQuery
  /** The operation the person asked for, or `null` while none is waiting. */
  requested: Ref<FileOperationOrder | null>
  /** Whether the listing is narrowed by a name search, so an empty answer means "no match". */
  isSearching: ComputedRef<boolean>
  /** Whether the latest answer arrived and holds no rows. */
  answeredNothing: ComputedRef<boolean>
  /** The most items one batch may name, once the server has said. */
  maxBatchItems: ComputedRef<number | null>
  /** Points the screen at another account's home. */
  changeAccount: (id: string) => Promise<void>
  /** Carries out the choice of a row's menu: editing is a navigation, the rest is handed on. */
  onEntryAction: (action: FileEntryAction, entry: FileEntry) => Promise<void>
  /** Asks for a new empty file or folder in the folder being browsed. */
  onCreate: (action: 'createFile' | 'createFolder') => void
  /** Hands a bulk action on, for the rows that are ticked. */
  onBatchAction: (action: FileBatchAction) => void
  /** Reads the folder again after an operation changed it, and drops ticks on rows that moved. */
  onOperationDone: () => Promise<void>
  /** Forgets the operation that was asked, once its dialog is over. */
  onOperationClosed: () => void
  /** Opens a file for editing, as pressing its name does. */
  openFile: (entry: FileEntry) => Promise<void>
  /** Reads the folder again. */
  refresh: () => Promise<void>
  /** Loads what the page needs once it is on screen. */
  start: () => void
}

/**
 * The state and actions of the file manager's browser page: the folder's server table, where it is
 * looking (the URL is the state), the operation asked for, and the handlers that route a row's menu
 * and the bulk bar. The page keeps only the layout.
 *
 * Presentation only: every request goes through the stores, and the server decides what an account
 * may see (rules/security.md item 2).
 * @returns The table, the location, the pending operation and the handlers.
 */
export const useFileBrowser = (): FileBrowser => {
  const router = useRouter()
  const store = useFilesStore()
  const optionsStore = useFilesOptionsStore()
  const transfers = useFileTransfersStore()
  const accountChoice = useAccountChoice()
  const { spec: accountSpec } = useAccountFilter()

  const table = useServerTable<FileEntry>({
    filters: fileBrowserFilters(accountSpec),
    /**
     * Reads one page of the folder through the store; with no account chosen yet there is nothing to ask.
     * @param request The page the URL describes.
     * @returns How the read ended.
     */
    load: (request: ListRequest): Promise<ListLoad<FileEntry>> => {
      const accountId = accountChoice.fixedAccountId.value ?? request.filters.account ?? ''
      if (accountId === '') {
        return Promise.resolve({
          kind: 'loaded',
          page: { items: [], total: 0, page: request.page, pageSize: request.pageSize },
        })
      }
      return store.list(accountId, request)
    },
  })

  const location = useFilePathQuery(table, accountChoice)

  /** The operation the person asked for, or `null` while none is waiting. */
  const requested: Ref<FileOperationOrder | null> = ref(null)

  /** Whether the listing is narrowed by a name search, so an empty answer means "no match". */
  const isSearching: ComputedRef<boolean> = computed(() => {
    return table.request.value.search !== ''
  })

  /** Whether the latest answer arrived and holds no rows. */
  const answeredNothing: ComputedRef<boolean> = computed(() => {
    return table.state.value.status === 'loaded' && table.total.value === 0
  })

  /** The most items one batch may name, once the server has said. */
  const maxBatchItems: ComputedRef<number | null> = computed(() => {
    return optionsStore.options?.maxBatchItems ?? null
  })

  /**
   * Points the screen at another account's home.
   * @param id The account chosen.
   * @returns Resolves once the URL has changed.
   */
  const changeAccount = async (id: string): Promise<void> => {
    // One account's rows must never sit under another's name while the new answer is on its way.
    store.reset()
    await location.changeAccount(id)
  }

  /**
   * Carries out the choice of a row's menu: editing is a navigation, the rest is handed on.
   * @param action What was chosen.
   * @param entry The row it was chosen on.
   * @returns Resolves once the navigation, if any, has settled.
   */
  const onEntryAction = async (action: FileEntryAction, entry: FileEntry): Promise<void> => {
    if (action === 'edit') {
      await router.push({ name: 'file-editor', query: { account: location.accountId.value, path: entry.path } })
      return
    }
    if (action === 'download') {
      // A download is a transfer, not a dialog: the browser takes the file and nothing else is asked.
      await transfers.download(location.accountId.value, entry.path)
      return
    }
    requested.value = { action, entries: [entry] }
  }

  /**
   * Asks for a new empty file or folder in the folder being browsed.
   * @param action Which of the two.
   * @returns Nothing.
   */
  const onCreate = (action: 'createFile' | 'createFolder'): void => {
    requested.value = { action, entries: [] }
  }

  /**
   * Hands a bulk action on, for the rows that are ticked.
   * @param action What was chosen.
   * @returns Nothing.
   */
  const onBatchAction = (action: FileBatchAction): void => {
    requested.value = { action, entries: store.selectedEntries }
  }

  /**
   * Reads the folder again.
   * @returns Resolves once the page settled.
   */
  const refresh = async (): Promise<void> => {
    await table.reload()
  }

  /**
   * Reads the folder again after an operation changed it, and drops ticks that acted on rows that moved.
   * @returns Resolves once the page settled.
   */
  const onOperationDone = async (): Promise<void> => {
    store.clearSelection()
    await refresh()
  }

  /**
   * Forgets the operation that was asked, once its dialog is over.
   * @returns Nothing.
   */
  const onOperationClosed = (): void => {
    requested.value = null
  }

  /**
   * Opens a file for editing, as pressing its name does.
   * @param entry The file.
   * @returns Resolves once the navigation has settled.
   */
  const openFile = async (entry: FileEntry): Promise<void> => {
    await onEntryAction('edit', entry)
  }

  /**
   * Loads the server's limits and points the URL at an account once the page is on screen.
   * @returns Nothing.
   */
  const start = (): void => {
    void optionsStore.load()
    void location.start()
  }

  return {
    accountChoice,
    table,
    location,
    requested,
    isSearching,
    answeredNothing,
    maxBatchItems,
    changeAccount,
    onEntryAction,
    onCreate,
    onBatchAction,
    onOperationDone,
    onOperationClosed,
    openFile,
    refresh,
    start,
  }
}
