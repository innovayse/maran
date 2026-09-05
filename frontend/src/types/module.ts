import type { ComputedRef } from 'vue'

/**
 * A module as the panel reports it to the interface: what it is, which licence tier it belongs
 * to, and whether the running licence currently permits it.
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
  /** Whether the running licence permits using it right now. */
  isEnabled: boolean
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
