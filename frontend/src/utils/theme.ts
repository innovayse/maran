import { THEME_PREFERENCES, type AppTheme, type ThemePreference } from '../types/theme'

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
 * Reads the theme preference the application should start with: a previously
 * chosen one, else `system` — which is what a reader who never chose has always
 * had, the operating system's preference.
 *
 * Never throws. Storage access fails outright in private modes and embedded
 * webviews, and a colour preference must not be able to break the shell.
 *
 * The storage key is a parameter rather than a constant here: the store owns
 * persistence and writes with that key, and a second copy in this file is how
 * a rename silently splits the read from the write.
 * @param storageKey Key the chosen preference was persisted under.
 * @returns The preference the application should start with.
 */
export const detectThemePreference = (storageKey: string): ThemePreference => {
  try {
    const stored = window.localStorage.getItem(storageKey)
    if (stored !== null && (THEME_PREFERENCES as readonly string[]).includes(stored)) {
      return stored as ThemePreference
    }
  } catch {
    // Storage unavailable — fall back to following the operating system.
  }

  return 'system'
}
