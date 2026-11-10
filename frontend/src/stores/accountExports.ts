import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useAccountMoveApi } from '../composables/apis/useAccountMoveApi'
import { ApiError } from '../composables/useApi'
import { useLatestRead } from '../composables/useLatestRead'
import { saveDownload } from '../utils/saveDownload'
import type {
  AccountExport,
  AccountExportAction,
  ExportGrant,
  ExportGrantSummary,
  IssueExportGrantRequest,
} from '../types/accountMove'
import type { ListLoad, ListRequest } from '../types/list'
import type { LoadingState } from '../types/loading'

/**
 * Owns the account exports an administrator works with (issue #127): one server page of the exports
 * list, one server page of the pull grants list, the one export the open screen names, and the actions
 * that make, download and grant it. The screens read state from here and call its actions; none
 * touches the API composable directly (rules/vue.md).
 *
 * A grant's token is held only in {@link issuedToken}, until the administrator dismisses it: it is
 * the one copy there will ever be, and no row of the grants list carries it.
 *
 * Error text is never generated here: when the backend refuses a request, its already-localized
 * `detail` is stored verbatim (rules/vue.md: "the backend owns their text").
 */
export const useAccountExportsStore = defineStore('accountExports', () => {
  const api = useAccountMoveApi()
  const latestExportsRead = useLatestRead()
  const latestGrantsRead = useLatestRead()

  /** The rows of the exports page last answered by the panel; empty before the first answer. */
  const exports: Ref<AccountExport[]> = ref([])

  /** The rows of the pull grants page last answered by the panel; empty before the first answer. */
  const grantRows: Ref<ExportGrantSummary[]> = ref([])

  /** The export the open screen names, as last read or made; `null` before it arrived or when the panel has none with that id. */
  const current: Ref<AccountExport | null> = ref(null)

  /** Where the read of {@link current} stands. A missing export (404) is `loaded` with a `null` {@link current}. */
  const currentState: Ref<LoadingState> = ref('loading')

  /** The panel's message for the last failed read of {@link current}, or `null`. */
  const errorMessage: Ref<string | null> = ref(null)

  /** The action in flight, or `null` when none runs. */
  const busy: Ref<AccountExportAction | null> = ref(null)

  /** The panel's message for the last refused action, or `null`. */
  const actionErrorMessage: Ref<string | null> = ref(null)

  /** The grant just issued, token included, until the administrator dismisses it; `null` otherwise. */
  const issuedToken: Ref<ExportGrant | null> = ref(null)

  /**
   * Loads one page of the exports into {@link exports} and reports how the read ended, for the
   * list page's `useServerTable`.
   * @param request The page, size, search and sort, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const listExports = async (request: ListRequest): Promise<ListLoad<AccountExport>> => {
    const isLatest = latestExportsRead.begin()
    try {
      const page = await api.listExports(request)
      // A slower, older read must not replace the rows of a newer one.
      if (isLatest()) {
        exports.value = page.items
      }
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Loads one page of the pull grants into {@link grantRows} and reports how the read ended, for
   * the grants page's `useServerTable`.
   * @param request The page, size, search, sort and the `state` filter, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const listGrants = async (request: ListRequest): Promise<ListLoad<ExportGrantSummary>> => {
    const isLatest = latestGrantsRead.begin()
    try {
      const page = await api.listGrants(request)
      if (isLatest()) {
        grantRows.value = page.items
      }
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Forgets the last refusal, so a screen opened a second time does not greet the administrator with
   * the previous attempt's error.
   * @returns Nothing.
   */
  const clearErrors = (): void => {
    actionErrorMessage.value = null
  }

  /**
   * Exports one account: the panel takes the backup and gathers every module's section, which can
   * take as long as the backup does.
   * @param accountId The account to export.
   * @returns The export, or `null` when the panel refused; the reason is in {@link actionErrorMessage}.
   */
  const create = async (accountId: string): Promise<AccountExport | null> => {
    busy.value = 'create'
    try {
      const made = await api.createExport(accountId)
      current.value = made
      currentState.value = 'loaded'
      issuedToken.value = null
      actionErrorMessage.value = null
      return made
    } catch (error) {
      actionErrorMessage.value = error instanceof ApiError ? error.message : null
      return null
    } finally {
      busy.value = null
    }
  }

  /**
   * Reads one export into {@link current}, fresh every time: an export expires.
   * @param id The export's identity.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadOne = async (id: string): Promise<void> => {
    // The export just made is already on screen; reading it again must not blank it.
    if (current.value?.id !== id) {
      current.value = null
      issuedToken.value = null
      currentState.value = 'loading'
    }
    try {
      current.value = await api.getExport(id)
      errorMessage.value = null
      currentState.value = 'loaded'
    } catch (error) {
      // No such export (expired, or never existed) is an answer, not a failure.
      if (error instanceof ApiError && error.status === 404) {
        current.value = null
        errorMessage.value = null
        currentState.value = 'loaded'
        return
      }
      errorMessage.value = error instanceof ApiError ? error.message : null
      currentState.value = 'error'
    }
  }

  /**
   * Fetches the open export's package and hands it to the browser's save flow.
   * @returns True when the file was saved; false when the panel refused (the reason is in {@link actionErrorMessage}).
   */
  const download = async (): Promise<boolean> => {
    const id = current.value?.id
    if (id === undefined) {
      return false
    }
    busy.value = 'download'
    try {
      saveDownload(await api.downloadPackage(id))
      actionErrorMessage.value = null
      return true
    } catch (error) {
      actionErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      busy.value = null
    }
  }

  /**
   * Issues a single-use pull grant for the open export. The token is held in {@link issuedToken}
   * until dismissed; the grants list reads the grant back without it.
   * @param request The address range and lifetime.
   * @returns True when the panel issued it; false when it refused (the reason is in {@link actionErrorMessage}).
   */
  const issueGrant = async (request: IssueExportGrantRequest): Promise<boolean> => {
    const id = current.value?.id
    if (id === undefined) {
      return false
    }
    busy.value = 'grant'
    try {
      const grant = await api.issueGrant(id, request)
      issuedToken.value = grant
      actionErrorMessage.value = null
      return true
    } catch (error) {
      actionErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      busy.value = null
    }
  }

  /**
   * Revokes a grant. The grants page reads its page again afterwards: the panel says what state the
   * grant is in now.
   * @param grantId The grant's identity.
   * @returns True when the panel revoked it; false when it refused (the reason is in {@link actionErrorMessage}).
   */
  const revokeGrant = async (grantId: string): Promise<boolean> => {
    busy.value = 'revoke'
    try {
      await api.revokeGrant(grantId)
      actionErrorMessage.value = null
      return true
    } catch (error) {
      actionErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      busy.value = null
    }
  }

  /**
   * Drops the token of the grant just issued from memory. After this the token exists nowhere.
   * @returns Nothing.
   */
  const dismissToken = (): void => {
    issuedToken.value = null
  }

  return {
    exports,
    grantRows,
    current,
    currentState,
    errorMessage,
    busy,
    actionErrorMessage,
    issuedToken,
    clearErrors,
    listExports,
    listGrants,
    create,
    loadOne,
    download,
    issueGrant,
    revokeGrant,
    dismissToken,
  }
})
