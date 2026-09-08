import { useI18n } from 'vue-i18n'

/** What a kind of backup storage is called and what it is, in the interface language. */
export interface BackupDestinationKindText {
  /**
   * The kind's name — "This server's disk" rather than `local`.
   * @param kind The kind exactly as the panel sent it.
   * @returns The name to render, or the machine name when this bundle has no word for it.
   */
  label: (kind: string) => string

  /**
   * The sentence explaining what the kind is and whether this build writes to it.
   * @param kind The kind exactly as the panel sent it.
   * @returns The explanation, or the empty string when this bundle has none for the kind.
   */
  description: (kind: string) => string
}

/**
 * Names and explains a kind of backup storage in the operator's language.
 *
 * A kind arrives as a machine-stable name, not as a sentence, which is what makes it this panel's
 * own chrome to translate rather than the backend's text to render verbatim — the same ground a
 * task kind, a task status and a backup reason are already translated on (rules/vue.md).
 *
 * **The raw name is the fallback, and it is not a formality.** The panel knows two kinds today and
 * the union that mirrors it will grow on the day the remote arm lands; until this bundle is
 * rebuilt, a kind it has no word for must reach the screen as the panel sent it rather than as a
 * dotted locale key. The description falls back to nothing instead, because a machine name printed
 * where a sentence belongs explains less than silence does.
 *
 * A composable rather than a component for the same reason as the task-kind one: both call sites
 * want TEXT, and one of them is the name beside a badge, where a component would leave the badge
 * describing something a screen reader cannot find.
 * @returns The two lookups, bound to the active locale.
 */
export const useBackupDestinationKindText = (): BackupDestinationKindText => {
  const i18n = useI18n()

  /**
   * The kind's name, or the machine name when there is no word for it.
   * @param kind The kind exactly as the panel sent it.
   * @returns The text to render.
   */
  const label = (kind: string): string => {
    const key = `backups.destinations.kinds.${kind}.label`
    return i18n.te(key) ? i18n.t(key) : kind
  }

  /**
   * The kind's explanation, or the empty string when there is none.
   * @param kind The kind exactly as the panel sent it.
   * @returns The text to render.
   */
  const description = (kind: string): string => {
    const key = `backups.destinations.kinds.${kind}.description`
    return i18n.te(key) ? i18n.t(key) : ''
  }

  return { label, description }
}
