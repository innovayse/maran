import { defineStore } from 'pinia'
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useMediaQuery } from '../composables/useMediaQuery'
import { applyTheme, detectThemePreference } from '../utils/theme'
import type { AppTheme, ThemePreference } from '../types/theme'

/**
 * Key the chosen preference is persisted under, so a reload keeps the user's choice.
 */
const STORAGE_KEY = 'maran.theme'

/** The operating system's light preference; anything else is read as dark, the baseline. */
const SYSTEM_LIGHT_QUERY = '(prefers-color-scheme: light)'

/**
 * The single source of truth for the interface theme. It holds the reader's
 * preference (light, dark, or follow the system), owns the `<html data-theme>`
 * attribute the design tokens key off, and persists the preference so a reload
 * does not throw the user back to the default.
 *
 * The attribute is written the moment the store is created rather than in a
 * mounted hook, and `main.ts` creates it at boot, before the app mounts: that is
 * what makes a chosen theme hold on signed-out pages, which never render the
 * shell, and keeps a user who chose light from seeing `index.html`'s baseline
 * dark past the first frame. With `system` chosen the painted theme also follows
 * a change of the operating system's preference while the panel is open.
 */
export const useThemeStore = defineStore('theme', () => {
  /** What the reader chose; `system` until they choose otherwise. */
  const preference: Ref<ThemePreference> = ref(detectThemePreference(STORAGE_KEY))

  /** Whether the operating system currently prefers light. */
  const systemLight: Readonly<Ref<boolean>> = useMediaQuery(SYSTEM_LIGHT_QUERY)

  /** The palette actually painted: the preference, with `system` resolved. */
  const current: ComputedRef<AppTheme> = computed(() => {
    if (preference.value === 'system') {
      return systemLight.value ? 'light' : 'dark'
    }
    return preference.value
  })

  /**
   * Records the reader's preference and remembers it; the painted theme follows through
   * {@link current}.
   *
   * @param next The preference the reader chose.
   * @returns Nothing; state and the DOM update synchronously.
   */
  const setPreference = (next: ThemePreference): void => {
    preference.value = next
    try {
      window.localStorage.setItem(STORAGE_KEY, next)
    } catch {
      // Persisting is a convenience: an unavailable storage must not break switching.
    }
  }

  // Applied at creation (`immediate`), not on mount: see the store's doc comment. `flush: 'sync'`
  // so a choice repaints in the same task as the click rather than a frame later.
  watch(current, applyTheme, { immediate: true, flush: 'sync' })

  return { preference, current, setPreference }
})
