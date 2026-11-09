import type { RouteLocationRaw } from 'vue-router'
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
  to: RouteLocationRaw
  /** The glyph in the row's icon square. */
  icon: UiIconName
  /** The row's text, already translated by the page. */
  label: string
  /** The module whose licence must permit the route, or `null` for a screen that is not a module. */
  module: string | null
}

/**
 * One shortcut of the catalogue in `router/quickActions.ts`, before it is translated: the closed
 * set the dashboard's quick-actions bar offers an administrator. The panel's layout names shortcuts
 * by `key`; the SPA maps the key to this row and skips a key it has no row for.
 *
 * A shortcut is a LINK to the screen that owns the work, never an endpoint of its own: the screen
 * enforces its own policy on the server, so a shortcut can only ever take somebody where they could
 * have gone by the menu. Creating something is `?action=create` on the owning screen, which opens
 * that screen's own form.
 */
export interface QuickActionSpec {
  /** Stable key; the panel's `DashboardActionKey`, the list key and the test id. */
  key: string
  /** Where it leads. */
  to: RouteLocationRaw
  /** The glyph beside the label. */
  icon: UiIconName
  /** The i18n key of the label. */
  labelKey: string
  /** The module whose licence must permit the route, or `null` for a screen that is not a module. */
  module: string | null
}
