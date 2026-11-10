import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useAccountMoveApi } from '../composables/apis/useAccountMoveApi'
import { ApiError } from '../composables/useApi'
import { useLatestRead } from '../composables/useLatestRead'
import type {
  AccountImport,
  AccountImportAction,
  AccountImportReport,
  AccountImportState,
  IssuedSecret,
  StartAccountImportRequest,
} from '../types/accountMove'
import type { ListLoad, ListRequest } from '../types/list'
import type { LoadingState } from '../types/loading'

/** The states in which an import no longer changes by itself, so the screen stops reading it. */
const FINISHED_STATES: readonly AccountImportState[] = ['Completed', 'RolledBack', 'RolledBackIncomplete', 'Discarded']

/**
 * Owns the account import an administrator is working with (issue #127): staging an uploaded package,
 * the dry-run report, the start, the progress of the job, and the one-time credentials. The screens
 * read state from here and call its actions; none touches the API composable directly (rules/vue.md).
 *
 * It holds one server page of the imports list and the one import the open screen names. The generated credentials are held in {@link secrets} only until the administrator dismisses them.
 *
 * Error text is never generated here: when the backend refuses a request, its already-localized
 * `detail` is stored verbatim (rules/vue.md: "the backend owns their text").
 */
export const useAccountImportsStore = defineStore('accountImports', () => {
  const api = useAccountMoveApi()
  const latestRead = useLatestRead()

  /** The rows of the imports page last answered by the panel; empty before the first answer. */
  const imports: Ref<AccountImport[]> = ref([])

  /** The import the open screen names, as last read; `null` before it arrived or when the panel has none with that id. */
  const current: Ref<AccountImport | null> = ref(null)

  /** Where the read of {@link current} stands. A missing import (404) is `loaded` with a `null` {@link current}. */
  const currentState: Ref<LoadingState> = ref('loading')

  /** The panel's message for the last failed read of {@link current}, or `null`. */
  const errorMessage: Ref<string | null> = ref(null)

  /** The action in flight, or `null` when none runs. */
  const busy: Ref<AccountImportAction | null> = ref(null)

  /** The panel's message for the last refused action, or `null`. */
  const actionErrorMessage: Ref<string | null> = ref(null)

  /** The latest dry-run report of {@link current}, or `null` before one ran. */
  const report: Ref<AccountImportReport | null> = ref(null)

  /** The credentials the import generated, held until dismissed; `null` while none were claimed. */
  const secrets: Ref<IssuedSecret[] | null> = ref(null)

  /**
   * Loads one page of the imports into {@link imports} and reports how the read ended, for the
   * list page's `useServerTable`.
   * @param request The page, size, search, sort and the `state` filter, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest): Promise<ListLoad<AccountImport>> => {
    const isLatest = latestRead.begin()
    try {
      const page = await api.listImports(request)
      // A slower, older read must not replace the rows of a newer one.
      if (isLatest()) {
        imports.value = page.items
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
   * Whether an import has reached a state it will not leave by itself.
   * @param state The import's state.
   * @returns True for completed, rolled-back and discarded imports.
   */
  const isFinished = (state: AccountImportState): boolean => {
    return FINISHED_STATES.includes(state)
  }

  /**
   * Uploads a package. The panel verifies its signature against the fingerprint before accepting it
   * and changes nothing about any account; the upload can take as long as the file is large.
   * @param file The package file.
   * @param fingerprint The sending server's key fingerprint, as read on that server.
   * @returns The staged import, or `null` when the panel refused; the reason is in {@link actionErrorMessage}.
   */
  const stage = async (file: Blob, fingerprint: string): Promise<AccountImport | null> => {
    busy.value = 'stage'
    try {
      const staged = await api.stageImport(file, fingerprint)
      current.value = staged
      currentState.value = 'loaded'
      report.value = null
      secrets.value = null
      actionErrorMessage.value = null
      return staged
    } catch (error) {
      actionErrorMessage.value = error instanceof ApiError ? error.message : null
      return null
    } finally {
      busy.value = null
    }
  }

  /**
   * Reads one import into {@link current}.
   * @param id The import's identity.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadOne = async (id: string): Promise<void> => {
    // The import just staged is already on screen; reading it again must not blank it or its report.
    if (current.value?.id !== id) {
      current.value = null
      report.value = null
      secrets.value = null
      currentState.value = 'loading'
    }
    try {
      current.value = await api.getImport(id)
      errorMessage.value = null
      currentState.value = 'loaded'
    } catch (error) {
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
   * Reads the open import again without blanking it, for the progress polling. A failure keeps what
   * is held: the next tick tries again.
   * @param id The import's identity.
   * @returns Resolves once the read has settled.
   */
  const refreshQuietly = async (id: string): Promise<void> => {
    try {
      current.value = await api.getImport(id)
    } catch {
      // Progress is advisory: the held import stays and the next tick reads again.
    }
  }

  /**
   * Runs the dry run of the open import against a plan. Nothing about any account changes.
   * @param id The import's identity.
   * @param planId The plan the account would be created against.
   * @returns True when the panel answered; false when it refused (the reason is in {@link actionErrorMessage}).
   */
  const loadReport = async (id: string, planId: string): Promise<boolean> => {
    busy.value = 'report'
    try {
      report.value = await api.getReport(id, planId)
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
   * Starts the job that creates the account. The panel compares the two retyped confirmations itself.
   * @param id The import's identity.
   * @param request The plan, the optional reseller and the confirmations.
   * @returns True when the job started; false when the panel refused (the reason is in {@link actionErrorMessage}).
   */
  const start = async (id: string, request: StartAccountImportRequest): Promise<boolean> => {
    busy.value = 'start'
    try {
      current.value = await api.startImport(id, request)
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
   * Discards the open import without starting it.
   * @param id The import's identity.
   * @returns True when the panel discarded it; false when it refused (the reason is in {@link actionErrorMessage}).
   */
  const discard = async (id: string): Promise<boolean> => {
    busy.value = 'discard'
    try {
      current.value = await api.discardImport(id)
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
   * Claims the credentials the import generated. The panel returns them once; they are held in
   * {@link secrets} until dismissed, and the import no longer reports any waiting.
   * @param id The import's identity.
   * @returns True when the credentials arrived; false when the panel refused (the reason is in {@link actionErrorMessage}).
   */
  const claimSecrets = async (id: string): Promise<boolean> => {
    busy.value = 'claim'
    try {
      secrets.value = await api.claimSecrets(id)
      if (current.value !== null) {
        current.value = { ...current.value, hasIssuedSecrets: false }
      }
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
   * Drops the credentials from memory. After this they exist nowhere.
   * @returns Nothing.
   */
  const dismissSecrets = (): void => {
    secrets.value = null
  }

  return {
    imports,
    current,
    currentState,
    errorMessage,
    busy,
    actionErrorMessage,
    report,
    secrets,
    clearErrors,
    list,
    isFinished,
    stage,
    loadOne,
    refreshQuietly,
    loadReport,
    start,
    discard,
    claimSecrets,
    dismissSecrets,
  }
})
