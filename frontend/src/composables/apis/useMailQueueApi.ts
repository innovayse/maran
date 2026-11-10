import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type { ListRequest } from '../../types/list'
import type { MailQueue, MailQueueApi, MailQueueCount, MailQueueFlush } from '../../types/mailQueue'

/** The endpoint the queue is listed and a message removed through. */
const MAIL_QUEUE_PATH = '/api/v1/mail-queue'

/** The narrow endpoint the navigation badge's figure is read from. */
const MAIL_QUEUE_COUNT_PATH = '/api/v1/mail-queue/count'

/** The endpoint that asks the mail transfer agent to retry every deferred message. */
const MAIL_QUEUE_FLUSH_PATH = '/api/v1/mail-queue/flush'

/** The queue list takes no filter: a search and a sort are all it narrows by. */
const NO_FILTER_PARAMS: Readonly<Record<string, string>> = {}

/**
 * Builds the mail queue API on top of the shared low-level client.
 * @returns The {@link MailQueueApi} bound to the panel's mail queue endpoints.
 */
export const useMailQueueApi = (): MailQueueApi => {
  const api = useApi()

  /**
   * Reads one page of the queue.
   * @param request The page, size, search and sort.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page, the queue's total and whether the list is truncated.
   */
  const list = (request: ListRequest, signal?: AbortSignal): Promise<MailQueue> => {
    return api.get<MailQueue>(`${MAIL_QUEUE_PATH}${listQuery(request, NO_FILTER_PARAMS)}`, signal)
  }

  /**
   * Reads how many messages the queue holds.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The count.
   */
  const count = (signal?: AbortSignal): Promise<MailQueueCount> => {
    return api.get<MailQueueCount>(MAIL_QUEUE_COUNT_PATH, signal)
  }

  /**
   * Removes one queued message.
   * @param queueId The mail transfer agent's id for the message.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel removed it; the answer is an empty 204.
   */
  const remove = (queueId: string, signal?: AbortSignal): Promise<void> => {
    return api.delete<void>(`${MAIL_QUEUE_PATH}/${encodeURIComponent(queueId)}`, signal)
  }

  /**
   * Asks the mail transfer agent to retry every deferred message now.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The queue size after the request.
   */
  const flush = (signal?: AbortSignal): Promise<MailQueueFlush> => {
    return api.post<MailQueueFlush>(MAIL_QUEUE_FLUSH_PATH, undefined, signal)
  }

  return { list, count, remove, flush }
}
