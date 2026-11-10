import type { ComputedRef } from 'vue'

/**
 * Whether a module the panel composed may be used right now. Two states and no third: a module the
 * panel did not compose is absent from the catalogue, which is how the interface tells "not
 * installed" from "installed but not covered". `lapsed` is a paid module whose licence or lease ran
 * out, and the interface treats it like `locked`.
 */
export type ModuleState =
  /** Usable. */
  | 'enabled'
  /** Composed but not covered: shown as locked and led to the upgrade page. */
  | 'locked'
  /** A paid module whose licence or lease ran out: drawn and guarded like a locked one. */
  | 'lapsed'

/**
 * A module as the panel reports it to the interface: what it is, which licence tier it belongs
 * to, and whether the panel lets it be used.
 *
 * The interface uses this only to decide what to show — the backend re-checks the licence on
 * every request, so a tampered response grants nothing (rules/architecture.md).
 */
export interface PanelModule {
  /** Stable machine name, matching the backend module (`sites`, `databases`, `backups`…). */
  name: string
  /**
   * Human-readable name, already localized by the backend in the request's language. Optional
   * until the backend ships the field; the machine `name` is the fallback. The frontend never
   * translates it — module names are server-side concepts, and marketplace modules are unknown
   * when this bundle is built (rules/vue.md).
   */
  displayName?: string
  /** Licence tier: `included` ships with every plan, other values name the plan that unlocks it. */
  tier: string
  /**
   * The same tier in the words an operator reads, already localized by the backend in the
   * request's language. Optional so a panel older than the field degrades to showing no tier
   * sentence rather than to printing `tier` — the machine constant — inside translated text, which
   * is the defect this field was added to end. The frontend never translates it: tiers are
   * server-side concepts and the SPA holds no list of them (rules/vue.md).
   */
  tierDisplayName?: string
  /**
   * Whether the module is part of the core or installed separately, in the words an operator reads,
   * already localized by the backend in the request's language. Optional for the same reason as
   * `tierDisplayName`. The machine `kind` stays on the wire for scripts; the SPA holds no list of
   * kinds (rules/vue.md).
   */
  kindDisplayName: string
  /** Whether the module may be used right now; the backend decides it in one place. */
  state: ModuleState
  /**
   * Until when the module's licence is valid, as an ISO 8601 timestamp, or `null` when it does not
   * expire or the module is not paid. Optional so a panel older than the field, and a fixture that
   * has no opinion, degrade to showing no date.
   */
  validUntil?: string | null
  /**
   * The version of the module the panel composed. Optional because a panel older than the field
   * omits it; it is part of a packaged module's frame address, never shown as a name.
   */
  version?: string
  /**
   * Where the module is in the panel's lifecycle ({@link ModuleLifecycle}). Optional for the same
   * reason as `version`: an absent value is read as `active`, which is what every compiled-in
   * module of a panel older than the field is.
   */
  lifecycle?: ModuleLifecycle
  /**
   * True when the module came from an installed package rather than being compiled into the panel.
   * Optional; absent means false. Only a packaged module is drawn from its own catalogue entry
   * (menu items and routes): a compiled-in module's screens are written in this bundle.
   */
  isPackaged?: boolean
  /**
   * How to open the module's own screen in an isolated frame, or `null` when it has none (a
   * compiled-in module, or one that is not active: degraded and failed modules are never framed).
   * Optional; absent means `null`.
   */
  spa?: ModuleSpa | null
  /**
   * The sentence explaining a `failed` or `degraded` module, localized by the backend in the
   * request's language, or `null` when there is nothing to explain. Rendered as text.
   */
  failureDisplay?: string | null
  /**
   * The navigation entries the module contributes, ordered by the backend and localized in the
   * request's language. Optional; absent means none.
   */
  menu?: ModuleMenuItem[]
  /** The screen routes the module declares, titles localized by the backend. Optional; absent means none. */
  routes?: ModuleRoute[]
}

/**
 * Where a module is in the panel's lifecycle, as the backend's `ModuleLifecycleState` names it.
 * Only `active` is framed; the other three are explained, never run.
 */
export type ModuleLifecycle =
  /** Installed but not yet composed (a restart is pending). Absent from menu and routes. */
  | 'installed'
  /** Composed and running. */
  | 'active'
  /** Composed but limited; led to the unavailable page. */
  | 'degraded'
  /** The loader refused it; explained by `failureDisplay`. */
  | 'failed'

/**
 * How the shell opens a packaged module's screen. Every member is built by the panel, never taken
 * from the package: the address in particular is `/module-frames/{id}/{version}/`.
 */
export interface ModuleSpa {
  /** The frame document's address, relative to the panel's origin, ending in a slash. */
  frameUrl: string
  /** `sha384-<base64>` of the module's entry file, from its signed manifest. */
  integrity: string
  /** The locales the module's bundle translates; always contains `en`. */
  locales: string[]
}

/**
 * One navigation entry a module contributes, as the backend reports it.
 */
export interface ModuleMenuItem {
  /** Stable key of the entry within the module. */
  key: string
  /** The `name` of one of the module's {@link ModuleRoute}s the entry opens. */
  routeName: string
  /** A glyph from the closed `ModuleIconKeys` vocabulary; an unknown key draws the neutral glyph. */
  iconKey: string
  /** The entry's label, already localized by the backend. Rendered as text. */
  label: string
  /** The entry's position among the module's entries. */
  order: number
}

/**
 * One screen route a module declares, as the backend reports it.
 */
export interface ModuleRoute {
  /** The route's name within the module. */
  name: string
  /** The module-relative path, starting with `/` (`/hello`). The shell mounts it at `/m/<id><path>`. */
  path: string
  /** The screen's title, already localized by the backend. Rendered as text. */
  title: string
  /** The module this screen additionally requires, or `null` when it needs none beyond its own. */
  requiresModule: string | null
}

/**
 * Typed access to the panel's module catalogue.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface ModulesApi {
  /**
   * Fetches every module the panel composed, with its licence state.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The module catalogue.
   */
  list: (signal?: AbortSignal) => Promise<PanelModule[]>
}

/**
 * Module availability checks for the interface: what to render, what to lock behind an upgrade
 * prompt, and what the router guard should let through.
 *
 * These checks are cosmetic. The backend enforces the licence on every request, so this composable
 * decides what a user *sees*, never what they *may do* (rules/architecture.md).
 */
export interface ModuleAccess {
  /** Modules the licence permits, in the order the panel reported them. */
  enabledModules: ComputedRef<PanelModule[]>
  /** Modules the panel knows but the licence does not permit — candidates for an upgrade prompt. */
  lockedModules: ComputedRef<PanelModule[]>
  /**
   * Whether a module may be used.
   * @param name Machine name of the module.
   * @returns True when the licence permits it.
   */
  canUse: (name: string) => boolean
  /**
   * Whether a module exists but is licence-locked.
   * @param name Machine name of the module.
   * @returns True when it should render as locked rather than absent.
   */
  isLocked: (name: string) => boolean
}
