import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useMailAliasesApi } from '../composables/apis/useMailAliasesApi'
import { useLatestRead } from '../composables/useLatestRead'
import { ApiError } from '../composables/useApi'
import type { ListLoad, ListRequest } from '../types/list'
import type { CreateMailAliasRequest, MailAlias, UpdateMailAliasRequest } from '../types/mailAlias'

/**
 * Owns one page of the aliases, forwarders and catch-alls and the create, edit and removal
 * workflows. The aliases page reads state from here and calls its actions — it never touches the
 * API composable directly (rules/vue.md).
 *
 * Error text is never generated here: the backend's already-localized `title`/`detail` is stored
 * verbatim (rules/vue.md: "the backend owns their text").
 */
export const useMailAliasesStore = defineStore('mailAliases', () => {
  const api = useMailAliasesApi()
  const latestRead = useLatestRead()

  /** The rows of the list page last answered by the panel; empty before the first answer. */
  const aliases: Ref<MailAlias[]> = ref([])

  /** True while a create request is in flight. */
  const creating: Ref<boolean> = ref(false)

  /** True while a mutation (edit, removal) is in flight. */
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
   * Loads one page of the alias list into {@link aliases} and reports how the read ended, for the
   * list page's `useServerTable`.
   * @param request The page, size, search, sort and filters, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest): Promise<ListLoad<MailAlias>> => {
    const isLatest = latestRead.begin()
    try {
      const page = await api.list(request)
      // A slower, older read must not replace the rows of a newer one.
      if (isLatest()) {
        aliases.value = page.items
      }
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Creates an alias, forwarder or catch-all. The list page then reads its page again, because where
   * the new row falls is the server's sort to say.
   * @param request The domain, kind, local part and targets.
   * @returns True when the panel created it — the caller reads {@link createErrorMessage} for the
   * reason when it did not.
   */
  const create = async (request: CreateMailAliasRequest): Promise<boolean> => {
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
   * Replaces an alias's destinations and replaces its held row with what the panel stored.
   * @param id The alias to change.
   * @param request The new destinations.
   * @returns True when the panel stored the change.
   */
  const update = async (id: string, request: UpdateMailAliasRequest): Promise<boolean> => {
    acting.value = true
    try {
      const stored = await api.update(id, request)
      aliases.value = aliases.value.map((alias) => {
        return alias.id === id ? stored : alias
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
   * Removes an alias, dropping it from the held page; the list page then reads the page again.
   * @param id The alias to remove.
   * @returns True when the panel removed it.
   */
  const remove = async (id: string): Promise<boolean> => {
    acting.value = true
    try {
      await api.remove(id)
      aliases.value = aliases.value.filter((alias) => {
        return alias.id !== id
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
    aliases,
    creating,
    acting,
    actionErrorMessage,
    createErrorMessage,
    clearActionError,
    list,
    create,
    update,
    remove,
  }
})
