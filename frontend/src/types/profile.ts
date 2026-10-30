/**
 * The signed-in person's own profile (rules/vue.md "Types"): the USER, for every role. It is a
 * different domain from a customer's hosting account (`types/account.ts`), which an administrator
 * does not have.
 */
import type { UserRole } from './auth'

/** The signed-in person's profile, as `GET /api/v1/profile` reports it. */
export interface Profile {
  /** The login name. */
  username: string
  /** The contact address password-reset links are sent to. */
  email: string
  /** What the user is allowed to reach. */
  role: UserRole
  /** True when a second factor is enrolled and required at sign-in. */
  isTwoFactorEnabled: boolean
  /** How many devices are signed in right now, this one included. */
  activeSessionCount: number
}

/** Public surface of the profile API composable. */
export interface ProfileApi {
  /** Reads the caller's own profile. */
  get: (signal?: AbortSignal) => Promise<Profile>
}
