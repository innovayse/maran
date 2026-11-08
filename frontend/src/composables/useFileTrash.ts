import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useAccountChoice, type AccountChoice } from './useAccountChoice'
import { useAccountFilter } from './useAccountFilter'
import { useAccountStart } from './useAccountStart'
import { useServerTable, type ServerTable } from './useServerTable'
import { useFileTrashStore } from '../stores/fileTrash'
import { useFilesOptionsStore } from '../stores/filesOptions'
import type { FileBatchResult, TrashEntry } from '../types/file'
import type { ListLoad, ListRequest } from '../types/list'

/** What {@link useFileTrash} hands the trash page. */
export interface FileTrash {
  /** Which account is looked at: fixed for a customer, chosen for an administrator. */
  accountChoice: AccountChoice
  /** The trash's server table: page, search, sort, state. */
  table: ServerTable
  /** The account whose trash is on screen: a customer's own, else the one the URL names. */
  accountId: ComputedRef<string>
  /** The label the account picker shows for the chosen account, which may not be on its page of matches. */
  selectedAccountLabel: ComputedRef<string | undefined>
  /** The outcome of the last restore when some entries could not be restored, or `null`. */
  restoreResult: Ref<FileBatchResult | null>
  /** Whether the empty-the-trash confirmation is open. */
  confirmingEmpty: Ref<boolean>
  /** Whether the search narrows the listing, so an empty answer means "no match". */
  isSearching: ComputedRef<boolean>
  /** Whether the latest answer arrived and holds no rows. */
  answeredNothing: ComputedRef<boolean>
  /** The panel's reason the last restore or empty failed, worded for the person; `null` otherwise. */
  failureMessage: ComputedRef<string | null>
  /** Whether a restore or an empty is in flight. */
  working: ComputedRef<boolean>
  /** Points the screen at another account's trash. */
  changeAccount: (id: string) => Promise<void>
  /** Restores the ticked entries to where they were, then reads the page again. */
  restoreSelected: () => Promise<void>
  /** Empties the trash for good once the person has confirmed, then reads the page again. */
  emptyTrash: () => Promise<void>
  /** Loads what the page needs once it is on screen. */
  start: () => void
}

/**
 * The state and actions of the trash page: the trash's server table (the URL is the state), the
 * ticked entries' restore and the confirmed empty. The page keeps only the layout.
 *
 * Presentation only: every request goes through the trash store, and the server decides which
 * account's trash may be read.
 * @returns The table, the account, the outcomes and the actions.
 */
export const useFileTrash = (): FileTrash => {
  const { t } = useI18n()
  const store = useFileTrashStore()
  const optionsStore = useFilesOptionsStore()
  const accountChoice = useAccountChoice()
  const { spec: accountSpec } = useAccountFilter()

  const table = useServerTable<TrashEntry>({
    filters: { account: accountSpec },
    /**
     * Reads one page of the trash through the store; with no account chosen yet there is nothing to ask.
     * @param request The page the URL describes.
     * @returns How the read ended.
     */
    load: (request: ListRequest): Promise<ListLoad<TrashEntry>> => {
      const chosen = accountChoice.fixedAccountId.value ?? request.filters.account ?? ''
      if (chosen === '') {
        return Promise.resolve({
          kind: 'loaded',
          page: { items: [], total: 0, page: request.page, pageSize: request.pageSize },
        })
      }
      return store.list(chosen, request)
    },
  })

  /** The outcome of the last restore when some entries could not be restored, or `null`. */
  const restoreResult: Ref<FileBatchResult | null> = ref(null)

  /** Whether the empty-the-trash confirmation is open. */
  const confirmingEmpty: Ref<boolean> = ref(false)

  /** The account whose trash is on screen: a customer's own, else the one the URL names. */
  const accountId: ComputedRef<string> = computed(() => {
    return accountChoice.fixedAccountId.value ?? table.filters.value.account
  })

  const accountStart = useAccountStart(table, accountChoice, accountId)

  /** Whether the search narrows the listing, so an empty answer means "no match". */
  const isSearching: ComputedRef<boolean> = computed(() => {
    return table.request.value.search !== ''
  })

  /** Whether the latest answer arrived and holds no rows. */
  const answeredNothing: ComputedRef<boolean> = computed(() => {
    return table.state.value.status === 'loaded' && table.total.value === 0
  })

  /** The panel's reason the last restore or empty failed, worded for the person; `null` otherwise. */
  const failureMessage: ComputedRef<string | null> = computed(() => {
    const submission = store.submission
    return submission.state === 'failed' ? (submission.message ?? t('common.list.unreachable')) : null
  })

  /** Whether a restore or an empty is in flight. */
  const working: ComputedRef<boolean> = computed(() => {
    return store.submission.state === 'sending'
  })

  /**
   * Points the screen at another account's trash.
   * @param id The account chosen.
   * @returns Resolves once the URL has changed.
   */
  const changeAccount = async (id: string): Promise<void> => {
    // One account's rows must never sit under another's name while the new answer is on its way.
    store.reset()
    restoreResult.value = null
    await table.setFilters({ ...table.filters.value, account: id })
  }

  /**
   * Restores the ticked entries to where they were, then reads the page again.
   * @returns Resolves once the request settled.
   */
  const restoreSelected = async (): Promise<void> => {
    const outcome = await store.restore(
      accountId.value,
      store.selectedEntries.map((entry) => {return entry.id}),
    )
    restoreResult.value = outcome !== null && outcome.items.some((item) => {return !item.succeeded}) ? outcome : null
    if (outcome !== null) {
      store.selectAll(false)
      await table.reload()
    }
  }

  /**
   * Empties the trash for good once the person has confirmed, then reads the page again.
   * @returns Resolves once the request settled.
   */
  const emptyTrash = async (): Promise<void> => {
    const count = await store.empty(accountId.value)
    confirmingEmpty.value = false
    if (count !== null) {
      restoreResult.value = null
      await table.reload()
    }
  }

  /**
   * Loads the server's limits and points the URL at an account once the page is on screen.
   * @returns Nothing.
   */
  const start = (): void => {
    void optionsStore.load()
    void accountStart.start()
  }

  return {
    accountChoice,
    table,
    accountId,
    selectedAccountLabel: accountStart.selectedAccountLabel,
    restoreResult,
    confirmingEmpty,
    isSearching,
    answeredNothing,
    failureMessage,
    working,
    changeAccount,
    restoreSelected,
    emptyTrash,
    start,
  }
}
