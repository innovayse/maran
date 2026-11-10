import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useMailQueueApi } from '../composables/apis/useMailQueueApi'
import { useLatestRead } from '../composables/useLatestRead'
import { ApiError } from '../composables/useApi'
import type { ListLoad, ListRequest } from '../types/list'
import type { MailQueueEntry } from '../types/mailQueue'

/**
 * Owns one page of the host's mail queue and the two things an administrator does to it: remove one
 * message, and ask for every deferred message to be retried now. The queue page reads state from
 * here and calls its actions; it never touches the API composable directly (rules/vue.md).
 *
 * Error text is never generated here: the backend's already-localized `title`/`detail` is stored
 * verbatim (rules/vue.md: "the backend owns their text"). A refused read is a display state and never
 * an empty page: a queue the panel could not read must not look like an empty queue.
 */
export const useMailQueueStore = defineStore('mailQueue', () => {
  const api = useMailQueueApi()
  const latestRead = useLatestRead()

  /** The rows of the page last answered by the panel; empty before the first answer. */
  const entries: Ref<MailQueueEntry[]> = ref([])

  /** How many messages the queue holds in all, as last answered, or `null` before the first answer. */
  const queueTotal: Ref<number | null> = ref(null)

  /** Whether the panel listed fewer messages than the queue holds, so the pages cover the newest only. */
  const truncated: Ref<boolean> = ref(false)

  /** True while a removal or a flush is in flight. */
  const acting: Ref<boolean> = ref(false)

  /** Backend-localized message from the most recent failed removal or flush, or `null`. Reported above the list. */
  const actionErrorMessage: Ref<string | null> = ref(null)

  /**
   * Forgets the last action's refusal, so it does not greet a dialog opened for another question.
   * @returns Nothing.
   */
  const clearActionError = (): void => {
    actionErrorMessage.value = null
  }

  /**
   * Loads one page of the queue into {@link entries} and reports how the read ended, for the list
   * page's `useServerTable`.
   * @param request The page, size, search and sort, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest): Promise<ListLoad<MailQueueEntry>> => {
    const isLatest = latestRead.begin()
    try {
      const queue = await api.list(request)
      // A slower, older read must not replace the rows of a newer one.
      if (isLatest()) {
        entries.value = queue.messages.items
        queueTotal.value = queue.queueTotal
        truncated.value = queue.truncated
      }
      return { kind: 'loaded', page: queue.messages }
    } catch (error) {
      // Rows from an earlier page must not stay on screen beside a failure message.
      if (isLatest()) {
        entries.value = []
        queueTotal.value = null
        truncated.value = false
      }
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Removes one queued message. The list page then reads its page again.
   * @param queueId The mail transfer agent's id for the message.
   * @returns True when the panel removed it; otherwise {@link actionErrorMessage} holds the reason.
   */
  const remove = async (queueId: string): Promise<boolean> => {
    acting.value = true
    try {
      await api.remove(queueId)
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
   * Asks the mail transfer agent to retry every deferred message now. The list page then reads its
   * page again.
   * @returns True when the panel passed the request on; otherwise {@link actionErrorMessage} holds the reason.
   */
  const flush = async (): Promise<boolean> => {
    acting.value = true
    try {
      await api.flush()
      actionErrorMessage.value = null
      return true
    } catch (error) {
      actionErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  return { entries, queueTotal, truncated, acting, actionErrorMessage, clearActionError, list, remove, flush }
})
