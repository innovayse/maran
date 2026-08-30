import type { RouteLocationRaw } from 'vue-router'

/**
 * Glyph a navigation entry is drawn with. Deliberately a closed union of the
 * two icons the shell actually draws today: the module catalogue reports no
 * icon of its own, so an entry's glyph is a presentation decision this bundle
 * makes, not data the backend sent.
 */
export type NavigationIcon =
  /** The shell's own always-present entries (system status). */
  | 'pulse'
  /** A module reported by the panel's catalogue. */
  | 'grid'

/**
 * One entry in the authenticated shell's sidebar navigation, as built by
 * `useNavigation` from the module catalogue.
 */
export interface NavigationEntry {
  /** Stable identifier for `:key` in `v-for`; the module name for module-backed entries. */
  key: string
  /**
   * Fully-resolved destination for this entry, including any route params. Built by the navigation
   * composable, which knows the module — the layout must not reconstruct it, or the two would
   * disagree (a module whose page is not registered yet needs the upgrade route WITH its param).
   */
  target: RouteLocationRaw
  /**
   * i18n key for the entry's label — used only by the shell's own entries, whose text this bundle
   * owns. Null for module entries, which carry a backend-localized {@link label} instead.
   */
  labelKey: string | null
  /**
   * Ready-to-render label supplied by the panel, already localized in the request's language. Set
   * for module entries — the SPA cannot own translations for modules it learns about at runtime,
   * including marketplace modules unknown when this bundle was built. Null for shell entries.
   */
  label: string | null
  /** Machine name of the module this entry represents, or `null` for entries not backed by a module (e.g. system status). */
  moduleName: string | null
  /**
   * Glyph the entry is drawn with. Not catalogue data — the panel reports no
   * icon — so the shell picks one from a closed set (see {@link NavigationIcon}).
   */
  icon: NavigationIcon
  /** Whether the licence does not currently permit this module — rendered as visibly locked, not hidden. */
  locked: boolean
}
