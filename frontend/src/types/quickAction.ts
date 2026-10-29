import type { UiIconName } from '../components/ui/UiIcon.vue'

/**
 * One row of the landing page's shortcuts card: where it goes, what it says, the glyph in its
 * square, and the module whose licence must permit it. A presentation shape only — the page builds
 * these from the licence catalogue and the caller's role; nothing is sent to or read from the
 * backend. It lives here rather than in the card's `.vue` file because the page that builds the
 * rows and the card that draws them both need it, and a type declared in one of them would be
 * copied into the other.
 */
export interface QuickAction {
  /** Stable key for the list. */
  key: string
  /** The route it leads to. */
  to: string
  /** The glyph in the row's icon square. */
  icon: UiIconName
  /** The row's text, already translated by the page. */
  label: string
  /** The module whose licence must permit the route, or `null` for a screen that is not a module. */
  module: string | null
}
