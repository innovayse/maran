/**
 * One ceiling a plan states for a resource, mirroring the backend's `ResourceCeilingDto`. Exactly
 * one of two shapes told apart by `kind`: the figure exists only on a `limited` ceiling, so "no
 * ceiling" is never read off a zero and an unlimited ceiling cannot carry a figure. Used for a plan's
 * CPU, memory and process ceilings (issue #133) and for the allowance beside an account's usage.
 */
export type ResourceCeiling =
  /** The plan caps the resource. */
  | {
      /** Discriminator: the plan states a cap. */
      kind: 'limited'
      /** The cap, in the unit of the resource the ceiling is for; always positive. */
      maximum: number
    }
  /** The plan states no cap for the resource. */
  | {
      /** Discriminator: the plan states no cap. */
      kind: 'unlimited'
    }
