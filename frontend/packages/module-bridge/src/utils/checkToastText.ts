import { MAX_TOAST_LENGTH } from '../constants/bridgeLimits'

/**
 * Checks the text of a toast: plain text of 1 to 200 characters. The shell shows it as text, never as markup.
 *
 * @param text The text to show.
 * @returns Null when acceptable, otherwise the reason it is refused.
 */
export const checkToastText = (text: string): string | null => {
  if (text.length < 1 || text.length > MAX_TOAST_LENGTH) {
    return `A toast must be 1 to ${MAX_TOAST_LENGTH} characters.`
  }

  return null
}
