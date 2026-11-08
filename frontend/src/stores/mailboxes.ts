import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useMailboxesApi } from '../composables/apis/useMailboxesApi'
import { ApiError } from '../composables/useApi'
import type { ListLoad, ListRequest } from '../types/list'
import type {
  ChangeMailboxPasswordRequest,
  CreateMailboxRequest,
  Mailbox,
  UpdateMailboxRequest,
} from '../types/mailbox'

/**
 * Owns one page of the mailboxes and the create, edit, password and removal workflows. The mailboxes
 * page reads state from here and calls its actions — it never touches the API composable directly
 * (rules/vue.md).
 *
 * **No password is kept.** The create and password requests carry one out and nothing comes back, so
 * there is no field in this store a password could be read from.
 *
 * Error text is never generated here: the backend's already-localized `title`/`detail` is stored
 * verbatim (rules/vue.md: "the backend owns their text").
 */
export const useMailboxesStore = defineStore('mailboxes', () => {
  const api = useMailboxesApi()

  /** The rows of the list page last answered by the panel; empty before the first answer. */
  const mailboxes: Ref<Mailbox[]> = ref([])

  /** True while a create request is in flight. */
  const creating: Ref<boolean> = ref(false)

  /** True while a mutation (edit, password change, removal) is in flight. */
  const acting: Ref<boolean> = ref(false)

  /** Backend-localized message from the most recent failed mutation, or `null`. Reported above the list. */
  const actionErrorMessage: Ref<string | null> = ref(null)

  /** Backend-localized message from the most recent failed create, or `null`. Kept apart from the list's own state. */
  const createErrorMessage: Ref<string | null> = ref(null)

  /**
   * Forgets the last mutation's refusal, so it does not greet a dialog opened for another question.
   * @returns Nothing.
   */
  const clearActionError = (): void => {
    actionErrorMessage.value = null
  }

  /**
   * Loads one page of the mailbox list into {@link mailboxes} and reports how the read ended, for
   * the list page's `useServerTable`.
   * @param request The page, size, search, sort and filters, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest): Promise<ListLoad<Mailbox>> => {
    try {
      const page = await api.list(request)
      mailboxes.value = page.items
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Creates a mailbox. The held page is not patched: where the new row falls is the server's sort to
   * say, so the list page reads its page again.
   * @param request The domain, local part, password and quota.
   * @returns True when the panel created the mailbox — the caller reads {@link createErrorMessage}
   * for the reason when it did not.
   */
  const create = async (request: CreateMailboxRequest): Promise<boolean> => {
    creating.value = true
    try {
      await api.create(request)
      createErrorMessage.value = null
      return true
    } catch (error) {
      createErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      creating.value = false
    }
  }

  /**
   * Changes a mailbox's limits and on/off state and replaces its held row with what the panel stored.
   * @param id The mailbox to change.
   * @param request The new limits and state.
   * @returns True when the panel stored the change.
   */
  const update = async (id: string, request: UpdateMailboxRequest): Promise<boolean> => {
    acting.value = true
    try {
      const stored = await api.update(id, request)
      mailboxes.value = mailboxes.value.map((mailbox) => {
        return mailbox.id === id ? stored : mailbox
      })
      actionErrorMessage.value = null
      return true
    } catch (error) {
      actionErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Sets a new password on a mailbox.
   * @param id The mailbox to re-credential.
   * @param request The new password.
   * @returns True when the panel stored it.
   */
  const changePassword = async (id: string, request: ChangeMailboxPasswordRequest): Promise<boolean> => {
    acting.value = true
    try {
      await api.changePassword(id, request)
      actionErrorMessage.value = null
      return true
    } catch (error) {
      actionErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Removes a mailbox, dropping it from the held page; the list page then reads the page again.
   * @param id The mailbox to remove.
   * @returns True when the panel removed it.
   */
  const remove = async (id: string): Promise<boolean> => {
    acting.value = true
    try {
      await api.remove(id)
      mailboxes.value = mailboxes.value.filter((mailbox) => {
        return mailbox.id !== id
      })
      actionErrorMessage.value = null
      return true
    } catch (error) {
      actionErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  return {
    mailboxes,
    creating,
    acting,
    actionErrorMessage,
    createErrorMessage,
    clearActionError,
    list,
    create,
    update,
    changePassword,
    remove,
  }
})
