import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { type AppLocale, SUPPORTED_LOCALES } from '../types/app'

/**
 * Key the chosen locale is persisted under, so a reload keeps the user's language.
 */
const STORAGE_KEY = 'maran.locale'

/**
 * The console message emitted when persisting the chosen language fails. A named
 * constant because the end-to-end suite asserts this exact text: the warning is
 * the one observable trace of a failure that is otherwise invisible until the
 * next reload boots the wrong language.
 */
const PERSIST_FAILURE_WARNING =
  'Maran: the chosen interface language could not be persisted; it applies now but will not survive a reload.'

/**
 * Reads the initial locale: a previously chosen one, else the browser's preference when it is
 * supported, else English. Never throws — storage access fails in private modes and embedded
 * webviews, and a language preference must not be able to break the shell.
 *
 * @returns The locale the application should start in.
 */
const detectInitialLocale = (): AppLocale => {
  try {
    const stored = window.localStorage.getItem(STORAGE_KEY)
    if (stored !== null && (SUPPORTED_LOCALES as readonly string[]).includes(stored)) {
      return stored as AppLocale
    }
  } catch {
    // Storage unavailable — fall through to the browser preference.
  }

  const browserLanguage = navigator.language.split('-')[0]
  return (SUPPORTED_LOCALES as readonly string[]).includes(browserLanguage)
    ? (browserLanguage as AppLocale)
    : 'en'
}

/**
 * The single source of truth for the interface language. Both the i18n instance (UI chrome) and
 * `useApi`'s `Accept-Language` header (server-produced messages) read from here, so the panel
 * never shows an English interface alongside Russian error text.
 *
 * Two seams of this store are about storage TRUTH, not convenience, and both exist because a
 * divergence between the visible language and the stored one was observed in the field (a tab
 * that showed Russian while storage held 'en', which the next full load then booted):
 *
 * - Persisting is verified by reading the value back, and a failure is surfaced
 *   ({@link PERSIST_FAILURE_WARNING} plus the `persistenceFailed` flag) instead of being
 *   swallowed — a write that silently failed is exactly the reload that "forgets" the language.
 * - A `storage` event from another same-origin tab is adopted live (last write wins), so two
 *   tabs can never disagree about the interface language until one of them happens to reload.
 */
export const useLocaleStore = defineStore('locale', () => {
  /** The active interface language. */
  const current: Ref<AppLocale> = ref(detectInitialLocale())

  /**
   * True when the last attempt to persist the chosen language did not stick — the write threw,
   * or the read-back did not return what was written. The interface still switches (switching
   * must never depend on storage), but the choice will not survive a reload. Exposed so the
   * shell can surface the condition to the user when a surface for it exists; until then it is
   * the store's machine-readable record of the failure, alongside the console warning.
   */
  const persistenceFailed: Ref<boolean> = ref(false)

  /** The value sent as `Accept-Language`, so the backend localizes its messages to match. */
  const acceptLanguageHeader: ComputedRef<string> = computed(() => {
    return `${current.value}, en;q=0.8`
  })

  /**
   * Persists the chosen language and verifies it actually stuck. Verified by reading the value
   * back rather than by the absence of a throw: embedded webviews and full-quota storage can
   * drop a write silently, and a silently dropped write is precisely the state that later boots
   * an unexpected language. On failure the in-memory locale is kept — the UI must still switch —
   * and the failure is surfaced instead of swallowed.
   *
   * @param locale The language to persist.
   * @returns Nothing; `persistenceFailed` records the outcome.
   */
  const persist = (locale: AppLocale): void => {
    let persisted = false
    try {
      window.localStorage.setItem(STORAGE_KEY, locale)
      persisted = window.localStorage.getItem(STORAGE_KEY) === locale
    } catch {
      // Storage unavailable — recorded below; an unavailable storage must not break switching.
      persisted = false
    }

    persistenceFailed.value = !persisted
    if (!persisted) {
      // A persistence failure has no UI surface yet and is invisible until the next reload
      // boots the wrong language; this warning is the one trace a live session (and the e2e
      // suite) can observe, which is why the no-console law is suspended for this line only.
      // eslint-disable-next-line no-console -- the sole observable surface of a silent persist failure
      console.warn(PERSIST_FAILURE_WARNING)
    }
  }

  /**
   * Switches the interface language and remembers the choice.
   *
   * @param locale The language to switch to.
   * @returns Nothing; state updates synchronously.
   */
  const setLocale = (locale: AppLocale): void => {
    current.value = locale
    persist(locale)
  }

  /**
   * Adopts a language chosen in another same-origin tab, so open tabs never disagree about the
   * interface language. Policy: LAST WRITE WINS — the newest choice, wherever it was made, is
   * what every tab shows. Adopting converges; the alternative (re-asserting this tab's value)
   * has two tabs overwriting each other forever, and it also only postpones the flip: the next
   * reload boots from storage anyway, so refusing to adopt merely turns a visible switch now
   * into a mystery switch later — which is the field defect this listener closes.
   *
   * A removed or unsupported value is ignored: this tab keeps its language, and the next boot
   * re-detects. Re-persisting over a removal is deliberately NOT done — a cleared storage may be
   * a deliberate privacy action, and fighting it wins nothing.
   *
   * Nothing is written back on adoption: the adopted value is already the stored one, and the
   * `storage` event never fires in the document that wrote it, so writing here would be churn.
   *
   * @param event The storage event fired by another same-origin document's write.
   * @returns Nothing; state updates synchronously.
   */
  const adoptExternalWrite = (event: StorageEvent): void => {
    // `key === null` means the whole storage area was cleared — a removal, ignored like one.
    if (event.key !== STORAGE_KEY) {
      return
    }
    const value = event.newValue
    if (value === null || !(SUPPORTED_LOCALES as readonly string[]).includes(value)) {
      return
    }
    if (value === current.value) {
      return
    }
    current.value = value as AppLocale
    // The adopted value is, by definition, persisted — whatever this flag recorded before no
    // longer describes reality.
    persistenceFailed.value = false
  }

  // Registered for the application's whole life and never removed: the locale store has no
  // unmount. The `storage` event only fires in OTHER documents, so this cannot observe (or
  // loop on) this tab's own writes. Guarded because this store is also constructed OUTSIDE a
  // browser: several e2e specs drive Pinia stores directly in the test process, where `window`
  // does not exist — and a windowless context has no other tabs to stay in step with anyway.
  if (typeof window !== 'undefined') {
    window.addEventListener('storage', adoptExternalWrite)
  }

  return { current, persistenceFailed, acceptLanguageHeader, setLocale }
})
