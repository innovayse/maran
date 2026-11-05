import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useSftpApi } from '../composables/apis/useSftpApi'
import { ApiError } from '../composables/useApi'
import type { LoadingState } from '../types/loading'
import type { AddSftpUserKeyRequest, SftpUser, SftpUserKey } from '../types/sftpUser'

/** The page sizes the keys panel offers; a login holds few keys, so the largest sizes are left out. */
const KEYS_PAGE_SIZES: readonly number[] = [10, 25, 50]

/**
 * Owns the public keys of the one SFTP login whose keys panel is open: the login, one server page
 * of its keys with the search and paging that produced it, and the add and remove requests. The
 * panel reads state from here and calls its actions; it never touches the API layer
 * (rules/vue.md: "API composables are called from Pinia stores ONLY").
 *
 * Kept apart from the logins store because it has a different lifetime: it exists only while a
 * panel is open, and {@link close} forgets all of it.
 *
 * Error text is never generated here: a refusal's already-localized message is stored verbatim.
 */
export const useSftpKeysStore = defineStore('sftpKeys', () => {
  const api = useSftpApi()

  /** The login whose keys are open, or `null` while no panel is. */
  const login: Ref<SftpUser | null> = ref(null)

  /** The keys on the page last answered, in the server's order; empty before the first answer. */
  const keys: Ref<SftpUserKey[]> = ref([])

  /** How many keys match the search across every page. */
  const total: Ref<number> = ref(0)

  /** The 1-based page asked for. */
  const page: Ref<number> = ref(1)

  /** Rows per page. */
  const pageSize: Ref<number> = ref(KEYS_PAGE_SIZES[0] ?? 10)

  /** The label search sent to the server; empty for none. */
  const search: Ref<string> = ref('')

  /** Where the read of the list stands. */
  const loadState: Ref<LoadingState> = ref('loading')

  /** The panel's reason the list was refused, already localized, or `null` (also when unreachable). */
  const loadErrorMessage: Ref<string | null> = ref(null)

  /** True while an add request is in flight. */
  const adding: Ref<boolean> = ref(false)

  /** True while a removal is in flight. */
  const removing: Ref<boolean> = ref(false)

  /** The most recent failed add's message, already localized, or `null`. */
  const addErrorMessage: Ref<string | null> = ref(null)

  /** The most recent failed removal's message, already localized, or `null`. */
  const removeErrorMessage: Ref<string | null> = ref(null)

  /** Counts reads so a slow answer to an older page or search cannot overwrite a newer one. */
  let readSequence = 0

  /**
   * Reads the current page of the open login's keys.
   * @returns Resolves once the read settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    const owner = login.value
    if (owner === null) {
      return
    }
    const sequence = ++readSequence
    loadState.value = 'loading'
    try {
      const answer = await api.listKeys(owner.id, {
        page: page.value,
        pageSize: pageSize.value,
        search: search.value,
      })
      if (sequence !== readSequence) {
        return
      }
      keys.value = answer.items
      total.value = answer.total
      loadErrorMessage.value = null
      loadState.value = 'loaded'
    } catch (error) {
      if (sequence !== readSequence) {
        return
      }
      loadErrorMessage.value = error instanceof ApiError ? error.message : null
      loadState.value = 'error'
    }
  }

  /**
   * Closes the panel and forgets everything it held, including any read still on its way.
   * @returns Nothing.
   */
  const close = (): void => {
    readSequence++
    login.value = null
    keys.value = []
    total.value = 0
    page.value = 1
    pageSize.value = KEYS_PAGE_SIZES[0] ?? 10
    search.value = ''
    loadState.value = 'loading'
    loadErrorMessage.value = null
    addErrorMessage.value = null
    removeErrorMessage.value = null
  }

  /**
   * Opens the keys of one login: forgets the previous panel's state and reads the first page.
   * @param owner The login whose keys to show.
   * @returns Resolves once the first read settled.
   */
  const open = async (owner: SftpUser): Promise<void> => {
    close()
    login.value = owner
    await load()
  }

  /**
   * Searches the keys by label, from the first page.
   * @param term The text to search for; empty for every key.
   * @returns Resolves once the read settled.
   */
  const searchKeys = async (term: string): Promise<void> => {
    search.value = term
    page.value = 1
    await load()
  }

  /**
   * Moves to another page of the same search.
   * @param next The 1-based page.
   * @returns Resolves once the read settled.
   */
  const setPage = async (next: number): Promise<void> => {
    page.value = next
    await load()
  }

  /**
   * Changes the page size and returns to the first page, since the old page number may not exist.
   * @param next The rows per page.
   * @returns Resolves once the read settled.
   */
  const setPageSize = async (next: number): Promise<void> => {
    pageSize.value = next
    page.value = 1
    await load()
  }

  /**
   * Adds a key to the open login, then reads the page again: where the new key falls is the
   * server's order to say. Adding never ends an open session.
   * @param request The label and the key line.
   * @returns True when the panel stored the key — the caller reads {@link addErrorMessage} when not.
   */
  const addKey = async (request: AddSftpUserKeyRequest): Promise<boolean> => {
    const owner = login.value
    if (owner === null) {
      return false
    }
    adding.value = true
    try {
      await api.addKey(owner.id, request)
      addErrorMessage.value = null
    } catch (error) {
      addErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      adding.value = false
    }
    await load()
    return true
  }

  /**
   * Removes a key from the open login; the panel ends that login's open sessions as part of it.
   * The page is read again so it refills from the next one.
   * @param keyId The key to remove.
   * @returns True when the panel removed it.
   */
  const removeKey = async (keyId: string): Promise<boolean> => {
    const owner = login.value
    if (owner === null) {
      return false
    }
    removing.value = true
    try {
      await api.removeKey(owner.id, keyId)
      removeErrorMessage.value = null
    } catch (error) {
      removeErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      removing.value = false
    }
    // Removing the last key of a page leaves it empty; step back so the reader is not shown nothing.
    if (keys.value.length === 1 && page.value > 1) {
      page.value--
    }
    await load()
    return true
  }

  return {
    pageSizes: KEYS_PAGE_SIZES,
    login,
    keys,
    total,
    page,
    pageSize,
    search,
    loadState,
    loadErrorMessage,
    adding,
    removing,
    addErrorMessage,
    removeErrorMessage,
    open,
    close,
    searchKeys,
    setPage,
    setPageSize,
    addKey,
    removeKey,
  }
})
