import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type { ListRequest, Paged } from '../../types/list'
import type { NotificationCount, NotificationsApi, PanelNotification } from '../../types/notification'

/** The endpoint the administrators' notification feed lives at. */
const FEED_PATH = '/api/v1/notifications/feed'

/**
 * Builds the notification feed API on top of the shared low-level client. Every call acts on the
 * caller's own sign-in, so none of them names a user.
 * @returns The {@link NotificationsApi} bound to the feed endpoints.
 */
export const useNotificationsApi = (): NotificationsApi => {
  const api = useApi()

  /**
   * Reads one page of the entries the caller has not dismissed, newest first.
   * @param request The page and page size.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page.
   */
  const list = (request: ListRequest, signal?: AbortSignal): Promise<Paged<PanelNotification>> => {
    return api.get<Paged<PanelNotification>>(`${FEED_PATH}${listQuery(request, {})}`, signal)
  }

  /**
   * Reads the badge figure.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The count and whether it was capped.
   */
  const count = (signal?: AbortSignal): Promise<NotificationCount> => {
    return api.get<NotificationCount>(`${FEED_PATH}/unread-count`, signal)
  }

  /**
   * Dismisses one entry for the caller.
   * @param id The entry.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves when the panel accepted it.
   */
  const dismiss = async (id: string, signal?: AbortSignal): Promise<void> => {
    await api.post<void>(`${FEED_PATH}/${encodeURIComponent(id)}/dismiss`, undefined, signal)
  }

  /**
   * Dismisses every entry the caller has not dismissed.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves when the panel accepted it.
   */
  const dismissAll = async (signal?: AbortSignal): Promise<void> => {
    await api.post<void>(`${FEED_PATH}/dismiss-all`, undefined, signal)
  }

  return { list, count, dismiss, dismissAll }
}
