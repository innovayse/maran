import { computed, type ComputedRef } from 'vue'
import { useViewAsStore } from '../stores/viewAs'

/**
 * Whether the current session is a view-as one, as the panel last reported it (`GET /api/v1/view-as`).
 * It is the one question the SPA asks to stop requesting what a view-as session is refused
 * (issues #415 and #513): a menu item, a route and a page's own reads all take the answer from here.
 *
 * Advice, never enforcement: the panel refuses a view-as session by endpoint on every request. The
 * answer errs permissive: until the panel has confirmed a session the flag is `false`, which costs
 * a refused request at worst.
 * @returns The flag, reactive to the start and the end of viewing.
 */
export const useViewingAs = (): ComputedRef<boolean> => {
  const viewAsStore = useViewAsStore()

  return computed((): boolean => {
    return viewAsStore.viewing !== null
  })
}
