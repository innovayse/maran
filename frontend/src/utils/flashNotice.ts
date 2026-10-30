/**
 * A one-time success notice carried across a navigation: the create form says "created", the page
 * it lands on shows it as a toast.
 *
 * Carried in the history entry's state rather than in the URL, so it never appears in an address
 * that is bookmarked or shared, and taken out once read, so going back and forward does not show the
 * same notice again. The text is already translated by the page that wrote it.
 */

/** The history-state field the notice travels in. */
const FLASH_NOTICE_KEY = 'flashNotice'

/**
 * Builds the `state` a `router.push` carries the notice in.
 * @param text The notice, already translated.
 * @returns The state object to pass as the navigation's `state`.
 */
export const flashNoticeState = (text: string): Record<string, string> => {
  return { [FLASH_NOTICE_KEY]: text }
}

/**
 * Reads the notice the navigation into this page carried, and removes it from the history entry.
 * @returns The notice, or `null` when the page was not reached from a create.
 */
export const takeFlashNotice = (): string | null => {
  const state: unknown = window.history.state
  if (typeof state !== 'object' || state === null) {
    return null
  }
  const notice: unknown = (state as Record<string, unknown>)[FLASH_NOTICE_KEY]
  if (typeof notice !== 'string') {
    return null
  }
  window.history.replaceState({ ...state, [FLASH_NOTICE_KEY]: null }, '')
  return notice
}
