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
