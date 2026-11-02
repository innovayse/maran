import type { RouteLocationRaw } from 'vue-router'
import type { PanelModule } from './module'

/**
 * Glyph a navigation entry is drawn with. A closed union of the icons the shell draws: the module
 * catalogue reports no icon, so an entry's glyph is a presentation decision this bundle states in
 * `router/navigationTree.ts`, not data the backend sent.
 */
export type NavigationIcon =
  /** The dashboard, the panel's home. */
  | 'house'
  /** A module the panel reported that the navigation tree does not know. */
  | 'grid'
  /** Accounts, plans and resellers. */
  | 'users'
  /** Sites and the domains they answer on. */
  | 'earth'
  /** DNS zones. */
  | 'globe'
  /** Mailboxes and mail delivery. */
  | 'mail'
  /** Databases and their users. */
  | 'database'
  /** File transfer logins and scheduled jobs: the ways into an account's files. */
  | 'folderKey'
  /** Backups. */
  | 'archive'
  /** The firewall and the sign-in policy. */
  | 'shieldCheck'
  /** The server fleet. */
  | 'server'
  /** Metrics, logs, background tasks and the audit journal. */
  | 'chartLine'
  /** The panel's own settings. */
  | 'settings'
  /** A customer's own activity: the background tasks of their account (issue #312). */
  | 'listChecks'
  /** A customer's own account: profile, sign-in security and sessions (issue #312). */
  | 'user'

/**
 * Who a navigation item is offered to. Mirrors the signed-in user's role; `reseller` is declared
 * ahead of the role itself (issue #179) so the reseller's view is a filter on this one tree rather
 * than a second tree. Offering is advice: every endpoint behind a page enforces the role itself.
 */
export type NavigationRole = 'admin' | 'reseller' | 'customer'

/**
 * Whether a page exists in this bundle yet. A planned page is drawn in the menu, disabled, so the
 * shape of the product is visible before every page is built; when its page lands, flipping this to
 * `available` with the page's route is the whole change.
 */
export type NavigationAvailability =
  /** The page is built and routed under this route name. */
  | { state: 'available'; route: string }
  /** The page is not built yet; `issue` is the GitHub issue that builds it, when one is filed. */
  | { state: 'planned'; issue: number | null }

/** A live count the shell can draw beside an item. Only counts the shell already holds are listed. */
export type NavigationBadgeKind =
  /** Background tasks that failed, as the panel counts them (`GET /api/v1/tasks/failed-count`). */
  'failedTasks'

/** Who may see an item, and what the panel must have for it to be usable. */
export interface NavigationGate {
  /** The roles the item is offered to. */
  roles: readonly NavigationRole[]
  /**
   * The licensed module the page belongs to, or null for the panel's own screens. An item whose
   * module the catalogue does not report is hidden; one whose module is disabled leads to the
   * module's upgrade page.
   */
  module: string | null
  /**
   * The hosting-plan feature the page needs, or null when it needs none. Matched against the
   * signed-in account's plan features where the panel reports them; while it does not, the item is
   * offered (advice errs permissive, rules/security.md).
   */
  planFeature: string | null
}

/** One page in the navigation tree. */
export interface NavigationPageSpec extends NavigationGate {
  /** Stable key, unique across the whole tree; used for `:key`, DOM ids and stored state. */
  key: string
  /** i18n key of the page's menu label; the menu is the SPA's own chrome. */
  labelKey: string
  /** Whether the page is built, and where it is routed. */
  availability: NavigationAvailability
  /** The live count drawn beside the item, or null. */
  badge: NavigationBadgeKind | null
}

/** A top-level item that is a single page: a direct link, never a submenu. */
export interface NavigationLinkSpec extends NavigationPageSpec {
  /** Discriminates a link from a section. */
  kind: 'link'
  /** Glyph drawn beside the label. */
  icon: NavigationIcon
}

/**
 * A top-level item with two or more pages, drawn as a submenu. It carries no gate of its own: it is
 * shown while at least one of its pages is offered, so a role's tree is a filter on the pages alone.
 */
export interface NavigationSectionSpec {
  /** Discriminates a section from a link. */
  kind: 'section'
  /** Stable key, unique across the whole tree. */
  key: string
  /** i18n key of the section's label. */
  labelKey: string
  /** Glyph drawn beside the label. */
  icon: NavigationIcon
  /** The section's pages, landing page first. */
  children: readonly NavigationPageSpec[]
}

/** A top-level item of the navigation tree. */
export type NavigationNodeSpec = NavigationLinkSpec | NavigationSectionSpec

/** A labelled run of top-level items. */
export interface NavigationGroupSpec {
  /** Stable key; the label is `app.nav.groups.<key>`. */
  key: string
  /** The group's items, in drawing order. */
  nodes: readonly NavigationNodeSpec[]
}

/** Where a built entry leads, or why it leads nowhere. */
export type NavigationEntryState =
  /** A usable page. */
  | { kind: 'open'; target: RouteLocationRaw }
  /** A page whose module the licence does not permit; the target is the module's upgrade page. */
  | { kind: 'locked'; target: RouteLocationRaw }
  /** A page not built yet: drawn disabled, with no destination at all. */
  | { kind: 'planned'; issue: number | null }

/** A live count drawn beside an entry, with what it counts so it can be announced. */
export interface NavigationBadge {
  /** What the count is of. */
  kind: NavigationBadgeKind
  /** The count; always above zero, since a zero is not drawn. */
  count: number
}

/** What the shell knows about the caller and the panel when it builds the navigation. */
export interface NavigationContext {
  /** The signed-in caller's role. */
  role: NavigationRole
  /** The module catalogue as the panel reported it, in its order. */
  catalogue: readonly PanelModule[]
  /** Module machine name to whether the licence enables it; a module absent here is not installed. */
  modules: ReadonlyMap<string, boolean>
  /**
   * The caller's plan features, or null while the panel does not report them; null offers every
   * item, since hiding is advice and errs permissive.
   */
  planFeatures: ReadonlySet<string> | null
  /** The live counts the shell holds, per badge kind. */
  badges: Readonly<Record<NavigationBadgeKind, number>>
  /**
   * Whether this bundle registers a route.
   * @param name The route name.
   * @returns True when the router knows it.
   */
  hasRoute: (name: string) => boolean
}

/** The text an entry is drawn with. */
export type NavigationLabel =
  /** An i18n key of the SPA's own chrome: every item of the tree. */
  | { kind: 'key'; key: string }
  /** Text the panel supplied, already localized: a catalogue module the tree does not know. */
  | { kind: 'text'; text: string }

/**
 * One entry in the authenticated shell's navigation, as `utils/buildNavigation.ts` builds it from
 * the tree for the signed-in role.
 */
export interface NavigationEntry {
  /** Stable identifier for `:key` in `v-for`; the tree key, or a module name for a catalogue entry. */
  key: string
  /** The group the entry is drawn under (`app.nav.groups.<groupKey>`). */
  groupKey: string
  /** Where the entry leads, or that it leads nowhere yet. */
  state: NavigationEntryState
  /** The text the entry is drawn with. */
  label: NavigationLabel
  /** Glyph the entry is drawn with. */
  icon: NavigationIcon
  /** The live count to draw beside the entry, or null when there is none to show. */
  badge: NavigationBadge | null
  /**
   * The section's pages, drawn as a collapsible submenu. One level deep. Empty for a link, for a
   * section left with one page (drawn as that page's link), and for every child.
   */
  children: NavigationEntry[]
}

/**
 * Where a screen sits in the navigation, as `utils/locateNavigation.ts` derives it from the tree:
 * the group, the top-level item, the submenu page, and whether the screen is a level below them (a
 * record, a form) that the navigation does not list itself.
 */
export interface NavigationLocation {
  /** Key of the group the item is drawn under (`app.nav.groups.<key>`). */
  groupKey: string
  /** Key of the top-level item. */
  entryKey: string
  /** The submenu page the screen is, or sits beneath; null for a single-page item. */
  child: NavigationPageSpec | null
  /** Whether the screen is below the destination the navigation lists (`meta.navigationParent`). */
  leaf: boolean
}
