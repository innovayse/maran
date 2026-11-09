/**
 * What the public demo reports about itself, from the angles the demo build needs it: the status
 * the anonymous endpoint answers, the identity published on the sign-in screen, the reasons a
 * refused setting gives, and the API that carries them (rules/vue.md "Types" — one domain per file).
 *
 * The demo build is the only one that ever reads any of this; the normal bundle contains none of
 * the code that does (`__MARAN_DEMO__`, see `vite.config.ts`).
 */

/** Which of the two published logins an identity is. */
export type DemoIdentityRole = 'administrator' | 'customer'

/**
 * One published demo login. The credentials are public by design: the demo has no secret, and the
 * sign-in screen is the only place they are shown.
 */
export interface DemoIdentity {
  /** Whether this login opens the administrator area or a customer's cabinet. */
  role: DemoIdentityRole
  /** The login name to type. */
  username: string
  /** The password to type. */
  password: string
}

/**
 * Why one area of the panel is read-only in the demo. The sentence is the backend's, already in the
 * caller's language, and is rendered as plain text beside the blocked controls.
 */
export interface DemoRefusalReason {
  /** The area the reason covers; the demo notice looks its reason up by this id. */
  area: string
  /** The localized sentence explaining the refusal. */
  reason: string
}

/** The body of `GET /api/v1/demo/status`, binding the backend's `DemoStatusDto`. */
export interface DemoStatus {
  /** Hours between two resets of the demo. */
  intervalHours: number
  /** When the demo was last reset, as an ISO-8601 instant. */
  lastResetAt: string
  /** When the demo will next be reset, as an ISO-8601 instant. */
  nextResetAt: string
  /** The published logins, administrator first. */
  identities: DemoIdentity[]
  /** The reasons the refused areas give. */
  refusalReasons: DemoRefusalReason[]
}

/** The calls the demo store makes, in one place the composable builds and the store consumes. */
export interface DemoApi {
  /** Reads the demo's status. Anonymous: it works before anyone has signed in. */
  status: (signal?: AbortSignal) => Promise<DemoStatus>
}
