import { SUPPORTED_THEMES, type AppTheme } from '../types/theme'

/**
 * Attribute on `<html>` the design's tokens key off: `src/assets/css/main.css`
 * defines the dark palette on `:root` and overrides it under
 * `html[data-theme='light']`.
 */
const THEME_ATTRIBUTE = 'data-theme'

/**
 * Writes the theme onto the document element, which is what actually switches
 * the token palette.
 *
 * A one-line function earns its own name because it is the single place that
 * knows how a theme reaches the page: every caller — the store's first apply
 * and every later change — goes through here, so the attribute and its spelling
 * cannot drift apart across call sites.
 * @param theme The theme to put on `<html data-theme>`.
 * @returns Nothing; the DOM is updated synchronously.
 */
export const applyTheme = (theme: AppTheme): void => {
  document.documentElement.setAttribute(THEME_ATTRIBUTE, theme)
}

/**
 * Reads the theme the application should start in: a previously chosen one,
 * else the operating system's preference, else dark — the design's baseline,
 * and what `index.html` has already painted.
 *
 * Never throws. Storage access fails outright in private modes and embedded
 * webviews, and a colour preference must not be able to break the shell.
 *
 * The storage key is a parameter rather than a constant here: the store owns
 * persistence and writes with that key, and a second copy in this file is how
 * a rename silently splits the read from the write.
 * @param storageKey Key the chosen theme was persisted under.
 * @returns The theme the application should start in.
 */
export const detectInitialTheme = (storageKey: string): AppTheme => {
  try {
    const stored = window.localStorage.getItem(storageKey)
    if (stored !== null && (SUPPORTED_THEMES as readonly string[]).includes(stored)) {
      return stored as AppTheme
    }
  } catch {
    // Storage unavailable — fall through to the operating system preference.
  }

  return window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark'
}
