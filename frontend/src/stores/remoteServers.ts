import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useRemoteServersApi } from '../composables/apis/useRemoteServersApi'
import { useLatestRead } from '../composables/useLatestRead'
import { ApiError } from '../composables/useApi'
import type { ListLoad, ListRequest } from '../types/list'
import type { LoadingState } from '../types/loading'
import type {
  DiscoveredAccount,
  DiscoveredAccountDetail,
  DiscoveryResult,
  RemovedRemoteServer,
  RemoteServer,
  RemoteServerAction,
  RemoteServerDetail,
  RemoteServerRequest,
} from '../types/migrationSource'

/**
 * Owns the remote servers (migration sources) an administrator registers: one server page of the
 * list at a time, the one server a detail screen opened, the add and edit workflow, the host-key,
 * test, discovery and removal actions, and the discovered accounts of the open server. The screens
 * read state from here and call its actions; none touches the API composable directly
 * (rules/vue.md).
 *
 * Error text is never generated here: when the backend refuses a request, its already-localized
 * `detail` is stored verbatim (rules/vue.md: "the backend owns their text").
 */
export const useRemoteServersStore = defineStore('remoteServers', () => {
  const api = useRemoteServersApi()
  const latestServerRead = useLatestRead()
  const latestAccountRead = useLatestRead()

  /** The rows of the server list page last answered by the panel; empty before the first answer. */
  const servers: Ref<RemoteServer[]> = ref([])

  /** The server the detail or edit screen opened, as last read; `null` before it arrived, after a failed read, or when the panel has none with that id. */
  const current: Ref<RemoteServerDetail | null> = ref(null)

  /** Where the read of {@link current} stands. A server the panel does not have (404) is `loaded` with a `null` {@link current}. */
  const currentState: Ref<LoadingState> = ref('loading')

  /** The panel's message for the last failed read of {@link current}, or `null`. */
  const errorMessage: Ref<string | null> = ref(null)

  /** True while an add or an edit is in flight (adding scans the server, so it can take seconds). */
  const saving: Ref<boolean> = ref(false)

  /** The panel's message for the last refused add or edit, or `null`. */
  const saveErrorMessage: Ref<string | null> = ref(null)

  /** The action on the open server in flight, or `null` when none runs. */
  const busy: Ref<RemoteServerAction | null> = ref(null)

  /** The panel's message for the last refused action (rescan, confirm, test, discover, remove), or `null`. */
  const actionErrorMessage: Ref<string | null> = ref(null)

  /** The outcome of the last discovery run on the open server, or `null` before one ran. */
  const discovery: Ref<DiscoveryResult | null> = ref(null)

  /** The rows of the discovered-accounts page last answered by the panel; empty before the first answer. */
  const accounts: Ref<DiscoveredAccount[]> = ref([])

  /** The discovered account the account detail opened, or `null` before it arrived or after a failed read. */
  const account: Ref<DiscoveredAccountDetail | null> = ref(null)

  /** Where the read of {@link account} stands. */
  const accountState: Ref<LoadingState> = ref('loading')

  /** The panel's message for the last failed read of {@link account}, or `null`. */
  const accountErrorMessage: Ref<string | null> = ref(null)

  /**
   * Loads one page of the server list into {@link servers} and reports how the read ended, for the
   * list page's `useServerTable`.
   * @param request The page, size, search, sort and filters (`state`), as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest): Promise<ListLoad<RemoteServer>> => {
    const isLatest = latestServerRead.begin()
    try {
      const page = await api.list(request)
      // A slower, older read must not replace the rows of a newer one.
      if (isLatest()) {
        servers.value = page.items
      }
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Loads one page of the open server's discovered accounts into {@link accounts}, for the
   * accounts table's `useServerTable`.
   * @param id The server's identity.
   * @param request The page, size, search, sort and filters (`has`), as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const listAccounts = async (id: string, request: ListRequest): Promise<ListLoad<DiscoveredAccount>> => {
    const isLatest = latestAccountRead.begin()
    try {
      const page = await api.listAccounts(id, request)
      if (isLatest()) {
        accounts.value = page.items
      }
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Reads one server into {@link current}, fresh every time: its state is a live fact.
   * @param id The server's identity.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadOne = async (id: string): Promise<void> => {
    currentState.value = 'loading'
    current.value = null
    discovery.value = null
    try {
      current.value = await api.get(id)
      errorMessage.value = null
      currentState.value = 'loaded'
    } catch (error) {
      // No such server (removed since the link was followed) is an answer, not a failure.
      if (error instanceof ApiError && error.status === 404) {
        errorMessage.value = null
        currentState.value = 'loaded'
        return
      }
      errorMessage.value = error instanceof ApiError ? error.message : null
      currentState.value = 'error'
    }
  }

  /**
   * Reads one discovered account into {@link account}.
   * @param id The server's identity.
   * @param accountId The discovered account's identity.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadAccount = async (id: string, accountId: string): Promise<void> => {
    accountState.value = 'loading'
    account.value = null
    try {
      account.value = await api.getAccount(id, accountId)
      accountErrorMessage.value = null
      accountState.value = 'loaded'
    } catch (error) {
      accountErrorMessage.value = error instanceof ApiError ? error.message : null
      accountState.value = 'error'
    }
  }

  /**
   * Forgets the last refusals, so a form or dialog opened a second time does not greet the
   * administrator with the previous attempt's error.
   * @returns Nothing.
   */
  const clearErrors = (): void => {
    saveErrorMessage.value = null
    actionErrorMessage.value = null
  }

  /**
   * Adds a server: the panel generates our key and scans the host keys, which can take seconds.
   * The list page is not patched; it reads its page again when it opens.
   * @param request The server's name, address, port, login and optional address limit.
   * @returns The stored server, or `null` when the panel refused; the reason is in {@link saveErrorMessage}.
   */
  const add = async (request: RemoteServerRequest): Promise<RemoteServerDetail | null> => {
    saving.value = true
    try {
      const created = await api.add(request)
      saveErrorMessage.value = null
      current.value = created
      currentState.value = 'loaded'
      return created
    } catch (error) {
      saveErrorMessage.value = error instanceof ApiError ? error.message : null
      return null
    } finally {
      saving.value = false
    }
  }

  /**
   * Changes a server and swaps the opened server for the panel's answer. A new host, port or login
   * clears the pinned key on the server; the answer says so.
   * @param id The server's identity.
   * @param request The new values.
   * @returns The server as it now stands, or `null` when the panel refused; the reason is in {@link saveErrorMessage}.
   */
  const edit = async (id: string, request: RemoteServerRequest): Promise<RemoteServerDetail | null> => {
    saving.value = true
    try {
      const updated = await api.edit(id, request)
      current.value = updated
      saveErrorMessage.value = null
      return updated
    } catch (error) {
      saveErrorMessage.value = error instanceof ApiError ? error.message : null
      return null
    } finally {
      saving.value = false
    }
  }

  /**
   * Reads the open server again without blanking it. A failure keeps what is held: the refusal
   * that led here is the message the screen shows.
   * @returns Resolves once the read has settled.
   */
  const refreshQuietly = async (): Promise<void> => {
    const id = current.value?.id
    if (id === undefined) {
      return
    }
    try {
      current.value = await api.get(id)
    } catch {
      // Nothing to store: the held server and the action's own message stay.
    }
  }

  /**
   * Runs one action that answers the server detail and swaps the opened server for it.
   * @param action Which action this is, shown in {@link busy} while it runs.
   * @param call The API call.
   * @returns True when the panel accepted it; false when it refused (the reason is in {@link actionErrorMessage}).
   */
  const act = async (action: RemoteServerAction, call: () => Promise<RemoteServerDetail>): Promise<boolean> => {
    busy.value = action
    try {
      current.value = await call()
      actionErrorMessage.value = null
      return true
    } catch (error) {
      actionErrorMessage.value = error instanceof ApiError ? error.message : null
      // A failed test or a changed key flips the server's state on the panel; read it again so the screen shows that state.
      await refreshQuietly()
      return false
    } finally {
      busy.value = null
    }
  }

  /**
   * Scans the open server's host keys again. Never pins one.
   * @param id The server's identity.
   * @returns True when the panel scanned; false when it refused.
   */
  const rescanHostKeys = (id: string): Promise<boolean> => {
    return act('rescan', () => {
      return api.rescanHostKeys(id)
    })
  }

  /**
   * Pins a scanned host key. The panel compares the fingerprint with its candidates itself.
   * @param id The server's identity.
   * @param fingerprint The fingerprint the administrator selected and retyped.
   * @returns True when the panel pinned it; false when it refused (a mismatch is a refusal).
   */
  const confirmHostKey = (id: string, fingerprint: string): Promise<boolean> => {
    return act('confirm', () => {
      return api.confirmHostKey(id, fingerprint)
    })
  }

  /**
   * Tests the connection with the pinned key.
   * @param id The server's identity.
   * @returns True when the panel ran the test and the server is ready; false when the panel refused or the test failed (the typed failure is on {@link current}).
   */
  const test = async (id: string): Promise<boolean> => {
    const accepted = await act('test', () => {
      return api.test(id)
    })
    return accepted && current.value?.state === 'ready'
  }

  /**
   * Removes a server and forgets it.
   * @param id The server's identity.
   * @returns The public key to take off the source, or `null` when the panel refused; the reason is in {@link actionErrorMessage}.
   */
  const remove = async (id: string): Promise<RemovedRemoteServer | null> => {
    busy.value = 'remove'
    try {
      const removed = await api.remove(id)
      servers.value = servers.value.filter((server) => {
        return server.id !== id
      })
      if (current.value?.id === id) {
        current.value = null
      }
      actionErrorMessage.value = null
      return removed
    } catch (error) {
      actionErrorMessage.value = error instanceof ApiError ? error.message : null
      return null
    } finally {
      busy.value = null
    }
  }

  /**
   * Runs discovery on the open server, then reads the server again for its count and time.
   * @param id The server's identity.
   * @returns The run's outcome, or `null` when the panel refused; the reason is in {@link actionErrorMessage}.
   */
  const discover = async (id: string): Promise<DiscoveryResult | null> => {
    busy.value = 'discover'
    try {
      const result = await api.discover(id)
      discovery.value = result
      actionErrorMessage.value = null
      await refreshQuietly()
      return result
    } catch (error) {
      actionErrorMessage.value = error instanceof ApiError ? error.message : null
      return null
    } finally {
      busy.value = null
    }
  }

  return {
    servers,
    current,
    currentState,
    errorMessage,
    saving,
    saveErrorMessage,
    busy,
    actionErrorMessage,
    discovery,
    accounts,
    account,
    accountState,
    accountErrorMessage,
    list,
    listAccounts,
    loadOne,
    loadAccount,
    clearErrors,
    add,
    edit,
    rescanHostKeys,
    confirmHostKey,
    test,
    remove,
    discover,
  }
})
