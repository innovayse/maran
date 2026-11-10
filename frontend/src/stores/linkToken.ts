import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { separateLinkToken } from '../utils/separateLinkToken'

/**
 * Holds the one-time token of a mailed link (password reset, invitation) in memory, and nowhere else.
 *
 * The token arrives in the address, because that is where the mail put it, and an address is read by
 * far more than the page: the browser sends it as the `Referer` of every request the page makes and
 * keeps it in its history. So `main.ts` calls {@link adopt} before the router installs, which moves
 * the token here and rewrites the address without it; the screens read it from this store. The cost
 * is that a reload of the page no longer carries the token, so the person opens the mail's link
 * again; a token kept in storage to avoid that would outlive the screen that needs it.
 */
export const useLinkTokenStore = defineStore('linkToken', () => {
  /** The adopted token, or the empty string while there is none. */
  const token: Ref<string> = ref('')

  /**
   * Moves the token out of the page's address into this store.
   *
   * The address is rewritten in place with `replaceState`, so no history entry holds the token and
   * no later request names it as its `Referer`. An address with no token is left alone.
   * @param location The page's address (`window.location`).
   * @param history The page's history (`window.history`).
   * @returns Nothing; the token is stored and the address rewritten.
   */
  const adopt = (
    location: Pick<Location, 'pathname' | 'search' | 'hash'>,
    history: Pick<History, 'replaceState' | 'state'>,
  ): void => {
    const separated = separateLinkToken(location.pathname, location.search, location.hash)
    if (separated === null) {
      return
    }
    token.value = separated.token
    history.replaceState(history.state, '', separated.address)
  }

  /**
   * Forgets the token once the screen that needed it has spent it.
   * @returns Nothing; the token is cleared.
   */
  const discard = (): void => {
    token.value = ''
  }

  return { token, adopt, discard }
})
