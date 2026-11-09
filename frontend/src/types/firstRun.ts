import type { DashboardSection } from './dashboard'

/**
 * One step of the first-run checklist, mirroring the backend's `FirstRunStepDto`. The steps, their
 * order and whether each is done are the panel's; the SPA words the step and maps its destination.
 */
export interface FirstRunStep {
  /**
   * Which step this is: `plan`, `account`, `site`, `backupDestination` or `certificate`. A key this
   * bundle has no wording for is left out of the card rather than drawn as a raw token.
   */
  key: string
  /** Whether the step is done; a step the panel could not check is unavailable, never "not done". */
  done: DashboardSection<boolean>
  /**
   * Where the step is done, as a closed name the panel sends (`plans`, `accounts`, `sites`,
   * `backupDestinations`, `certificates`), never a URL. The SPA maps it to a route it has.
   */
  routeName: string
}

/** The first-run checklist, mirroring the backend's `FirstRunChecklistDto`. */
export interface FirstRunChecklist {
  /** The steps, in the order a server is built up. */
  steps: FirstRunStep[]
  /** True when every step is done and the card has nothing left to say. */
  complete: boolean
}

/**
 * Typed access to the first-run checklist endpoint.
 *
 * Called from Pinia stores only, never from a component (rules/vue.md).
 */
export interface FirstRunApi {
  /**
   * Reads the checklist for the signed-in administrator.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The steps and whether all are done.
   */
  get: (signal?: AbortSignal) => Promise<FirstRunChecklist>
}
