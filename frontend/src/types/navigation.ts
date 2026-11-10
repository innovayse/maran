import type { RouteLocationRaw } from 'vue-router'
import type { AccountFeatureName } from './accountFeature'
import type { ModuleState, PanelModule } from './module'

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

/**
 * A live count the shell can draw beside an item. A kind is listed when a page carries it in the
 * tree; whether the shell can feed it yet is a separate matter (see {@link NavigationContext.badges}).
 */
export type NavigationBadgeKind =
  /** Background tasks that failed, as the panel counts them (`GET /api/v1/tasks/failed-count`). */
  | 'failedTasks'
  /** Things on the server asking to be looked at (`GET /api/v1/dashboard/attention-count`). */
  | 'needsAttention'
  /** Messages waiting in the mail queue (`GET /api/v1/mail-queue/count`, administrators only). */
  | 'mailQueue'
  /** Package updates waiting to be installed (`GET /api/v1/monitoring/updates/count`, read from the package cache). */
  | 'availableUpdates'

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
  planFeature: AccountFeatureName | null
  /**
   * Whether the page is offered only to the account's owner and not to a member the owner invited.
   * Advice: the endpoint behind the page refuses a member regardless of what the menu shows.
   */
  ownerOnly: boolean
  /**
   * Whether the endpoints behind the page refuse a view-as session (`ViewAsAccess.Refused`,
   * issue #334), so the page cannot work while an operator is viewing as a login. Such a page is not
   * offered during viewing (issue #513). Advice: the panel refuses the request regardless, and a page
   * left unflagged costs a refused request, never a leak.
   */
  viewAsRefused: boolean
  /**
   * The site runtime the page is about, or null when it is about none. A page about PHP is offered
   * only while the host has a PHP version installed (issue #517). Advice: the endpoints answer
   * `apps.notInstalled` regardless, and while the panel has not reported its runtimes the page is
   * offered, since hiding is advice and errs permissive.
   */
  runtime: NavigationRuntime | null
}

/** The runtimes a menu item can depend on; the apps the stack status reports versions of. */
export type NavigationRuntime = 'php'

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
  /**
   * i18n key of the extra words the jump-to palette finds the page by (what an operator calls it:
   * "firewall", "cron", "ssl"), or null when the label says it all. The label stays what the menu
   * draws; these words are only searched, never drawn.
   */
  searchTermsKey: string | null
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

/**
 * What the panel reported about the plan features of the signed-in caller. The unreported arm
 * carries no set at all, so nothing can mistake "not reported" for "a plan with no features".
 */
export type NavigationPlanFeatures =
  | {
      /** The panel does not report them (an administrator, a read still on its way or failed). */
      state: 'unreported'
    }
  | {
      /** The panel reported the features of the caller's plan. */
      state: 'reported'
      /** The single feature ids the plan includes. */
      features: ReadonlySet<AccountFeatureName>
    }

/**
 * What the panel reported about the caller's standing on the account they act for. The unreported
 * arm carries nothing, so "not reported" cannot be mistaken for a member holding no features.
 */
export type NavigationAccountAccess =
  | {
      /** The panel does not report it (an administrator, a read still on its way or failed). */
      state: 'unreported'
    }
  | {
      /** The caller owns the account: nothing is held back from them by a grant. */
      state: 'owner'
    }
  | {
      /** The caller is a member of the account and may use only what the owner granted. */
      state: 'member'
      /** The single features the owner granted. */
      features: ReadonlySet<AccountFeatureName>
    }

/**
 * The grants one menu is drawn from: the plan's features and the caller's standing, taken together
 * for one account. They are replaced as a pair, so a menu is never built from the plan of one
 * account and the grants of another.
 */
export interface NavigationGrants {
  /** What the panel reported about the plan features. */
  planFeatures: NavigationPlanFeatures
  /** What the panel reported about the caller's standing. */
  accountAccess: NavigationAccountAccess
}

/**
 * Which runtimes the host has, as the stack status reports them: unreported until the panel has
 * answered, which offers every runtime page rather than hiding them.
 */
export type NavigationRuntimes =
  | {
      /** The panel has not said (not asked yet, or the read failed). */
      state: 'unreported'
    }
  | {
      /** The panel answered. */
      state: 'reported'
      /** The runtimes with at least one installed version. */
      installed: ReadonlySet<NavigationRuntime>
    }

/** What the shell knows about the caller and the panel when it builds the navigation. */
export interface NavigationContext {
  /** The signed-in caller's role. */
  role: NavigationRole
  /** Whether the session is a view-as one, as the panel last reported it; see {@link NavigationGate.viewAsRefused}. */
  viewingAs: boolean
  /** The module catalogue as the panel reported it, in its order. */
  catalogue: readonly PanelModule[]
  /** Module machine name to how the licence stands; a module absent here is not installed. */
  modules: ReadonlyMap<string, ModuleState>
  /**
   * The caller's plan features. While the panel does not report them every item is offered, since
   * hiding is advice and errs permissive.
   */
  planFeatures: NavigationPlanFeatures
  /**
   * The caller's standing on the account. While the panel does not report it every item is offered,
   * since hiding is advice and errs permissive.
   */
  accountAccess: NavigationAccountAccess
  /** Which runtimes the host has; see {@link NavigationRuntimes}. */
  runtimes: NavigationRuntimes
  /**
   * The live counts the shell holds, per badge kind. A partial map and not a total one: a kind is
   * present exactly when the panel answered with a real count, and absent when it was not asked,
   * has not answered yet, could not be read, or has no source at all. That keeps "no figure" a
   * different thing from a figure, without a number standing in for it (not `0`, which is a real
   * answer, and not `-1`). A per-kind union of loaded, loading and failed would add cases the
   * navigation treats identically — draw no badge — and nothing else reads them.
   */
  badges: Readonly<Partial<Record<NavigationBadgeKind, number>>>
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
  /** i18n key of the extra words the jump-to palette searches besides the label, or null. */
  searchTermsKey: string | null
  /**
   * The section's pages, drawn as a collapsible submenu. One level deep. Empty for a link, for a
   * section left with one page (drawn as that page's link), and for every child.
   */
  children: NavigationEntry[]
}

/**
 * What the shell can say about its navigation right now. `loading` means exactly one thing: a
 * customer's first answer for the account they act for (the plan read and the standing read) is
 * still on its way and there are no earlier grants to keep, so any menu drawn now would be a guess
 * the answer then shrinks. Operators and logins that act for no account are always `ready`, and a
 * read that failed settles as `ready` with every item offered (advice errs permissive,
 * rules/security.md). A menu is never drawn empty to mean "wait": waiting is its own arm.
 */
export type NavigationState =
  /** The first answer for a customer's active account is still on its way; there are no entries yet. */
  | { state: 'loading' }
  /** The menu is known; `entries` are in drawing order. */
  | { state: 'ready'; entries: NavigationEntry[] }

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
