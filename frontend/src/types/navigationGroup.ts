import type { NavigationEntry } from './navigation'

/**
 * One labelled run of sidebar entries, as built by `utils/groupNavigation.ts`. It is a
 * presentation shape only: the catalogue knows nothing about groups, so this exists for the
 * shell's expanded navigation and is never sent to or read from the backend.
 */
export interface NavigationGroup {
  /** Stable identifier for `:key` and for the label element's id; the group's name in the map. */
  key: string
  /** i18n key of the group's small uppercase label (`app.nav.groups.<key>`). */
  labelKey: string
  /** The entries in this group, in the order the catalogue gave them. Never empty. */
  entries: NavigationEntry[]
}
