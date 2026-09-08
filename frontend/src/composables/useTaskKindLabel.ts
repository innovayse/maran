import { useI18n } from 'vue-i18n'

/**
 * Names a task kind in the operator's language: "Taking a backup" rather than `BackupCreate`.
 *
 * A kind arrives as a machine-stable name, not as a sentence, which is what makes it this panel's
 * own chrome to translate rather than the backend's text to render verbatim — exactly the ground a
 * task status, an account state, a site backend and a backup reason are already translated on
 * (rules/vue.md).
 *
 * **The raw name is the fallback, and it is not a formality.** `TaskKinds` is a list of string
 * constants rather than an enum precisely so a module compiled after this bundle can record a kind
 * this bundle has no word for; the catalogue is asked first so such a kind reaches the screen as
 * the panel sent it instead of as a dotted locale key.
 *
 * A composable rather than a component because both call sites want TEXT — one of them is a
 * heading, and a heading whose only child is a component has no content a screen reader can find.
 * @returns A function from a kind to the text to show for it.
 */
export const useTaskKindLabel = (): ((kind: string) => string) => {
  const i18n = useI18n()

  /**
   * The kind in the operator's language, or the machine name when there is no word for it.
   * @param kind The kind exactly as the panel sent it.
   * @returns The text to render.
   */
  const label = (kind: string): string => {
    const key = `tasks.kinds.${kind}`
    return i18n.te(key) ? i18n.t(key) : kind
  }

  return label
}
