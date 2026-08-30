import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { applyTheme, detectInitialTheme } from '../utils/theme'
import type { AppTheme } from '../types/theme'

/**
 * Key the chosen theme is persisted under, so a reload keeps the user's choice.
 */
const STORAGE_KEY = 'maran.theme'

/**
 * The single source of truth for the interface theme. It owns the
 * `<html data-theme>` attribute the design tokens key off, and persists the
 * choice so a reload does not throw the user back to the default.
 *
 * The attribute is written the moment the store is created rather than in a
 * mounted hook: `index.html` paints dark first, so a user who chose light
 * would otherwise see a dark flash before the app caught up.
 */
export const useThemeStore = defineStore('theme', () => {
  /** The active interface theme. */
  const current: Ref<AppTheme> = ref(detectInitialTheme(STORAGE_KEY))

  // Applied at creation, not on mount: see the store's doc comment.
  applyTheme(current.value)

  /** Whether the dark palette is active — the shape a toggle control needs. */
  const isDark: ComputedRef<boolean> = computed(() => current.value === 'dark')

  /**
   * Switches the interface theme, applies it to the document and remembers it.
   *
   * @param theme The theme to switch to.
   * @returns Nothing; state and the DOM update synchronously.
   */
  const setTheme = (theme: AppTheme): void => {
    current.value = theme
    applyTheme(theme)
    try {
      window.localStorage.setItem(STORAGE_KEY, theme)
    } catch {
      // Persisting is a convenience: an unavailable storage must not break switching.
    }
  }

  /**
   * Flips between the two themes — what the shell's single toggle button does.
   *
   * @returns Nothing; delegates to {@link setTheme}.
   */
  const toggle = (): void => {
    setTheme(current.value === 'dark' ? 'light' : 'dark')
  }

  return { current, isDark, setTheme, toggle }
})
