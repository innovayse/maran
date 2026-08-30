/**
 * Every type the authentication domain needs, grouped in one file the way the
 * frontend groups types by domain (rules/vue.md "Types").
 */

/** What a signed-in user is allowed to reach. Mirrors the backend's `UserRole`. */
export type UserRole = 'admin' | 'customer'

/** The person the panel has signed in. */
export interface AuthenticatedUser {
  /** The user's identity. */
  id: string
  /** The login name, shown in the shell's user block. */
  username: string
  /** The contact address. */
  email: string
  /** What the user is allowed to reach, so the shell can hide what it must not offer. */
  role: UserRole
  /** The hosting account a customer owns; `null` for an administrator. */
  accountId: string | null
}

/**
 * The body of a successful sign-in or refresh. There is deliberately no refresh
 * token here: it lives in an httpOnly cookie the page's JavaScript cannot read.
 */
export interface LoginResult {
  /** The signed access token, or `null` when a second factor is still owed. */
  accessToken: string | null
  /** When that token expires, so the app can refresh before a call fails. */
  expiresAt: string | null
  /** True when the password was right but a code is required. */
  twoFactorRequired: boolean
  /** Who signed in, or `null` while the sign-in is incomplete. */
  user: AuthenticatedUser | null
}

/** Credentials for the first step of signing in. */
export interface LoginRequest {
  /** The login name. */
  username: string
  /** The plaintext password. Held only for the duration of the request. */
  password: string
}

/** Credentials plus the second factor, for the step that completes a sign-in. */
export interface VerifyTwoFactorRequest extends LoginRequest {
  /** A code from the authenticator app, or one of the user's recovery codes. */
  code: string
}

/** What the panel needs to create its first administrator. */
export interface CompleteSetupRequest {
  /** The token from the installer's one-time link. */
  token: string
  /** The administrator's login name. */
  username: string
  /** The administrator's contact address. */
  email: string
  /** The chosen password. */
  password: string
}

/** Whether the panel still needs its first administrator. */
export interface SetupState {
  /** True once any user exists. */
  isComplete: boolean
}

/** One signed-in device, as shown on the sessions screen. */
export interface Session {
  /** The session's identity, used to revoke it. */
  id: string
  /** When the device signed in. */
  issuedAt: string
  /** When it will be signed out unless it refreshes. */
  expiresAt: string
  /** Where it signed in from. */
  ipAddress: string
  /** What client it signed in with. */
  userAgent: string
  /** True for the device making this request. */
  isCurrent: boolean
}

/** The secret an authenticator app needs, handed out before anything is enabled. */
export interface TotpEnrolment {
  /** The base32 shared secret, for typing in by hand. */
  secret: string
  /** The same secret as an `otpauth://` URI, for the QR code. */
  provisioningUri: string
}

/** The recovery codes, returned the one and only time they are readable. */
export interface RecoveryCodes {
  /** The plaintext codes, in the order they should be shown. */
  codes: string[]
}

/** Public surface of the authentication API composable. */
export interface AuthApi {
  /** Signs in with a username and password. */
  login: (request: LoginRequest, signal?: AbortSignal) => Promise<LoginResult>
  /** Completes a sign-in that stopped for a second factor. */
  verifyTwoFactor: (request: VerifyTwoFactorRequest, signal?: AbortSignal) => Promise<LoginResult>
  /** Exchanges the refresh cookie for a new access token. */
  refresh: (signal?: AbortSignal) => Promise<LoginResult>
  /** Signs out of this device. */
  logout: (signal?: AbortSignal) => Promise<boolean>
  /** Signs out of every device. */
  logoutEverywhere: (signal?: AbortSignal) => Promise<boolean>
  /** Reports whether the panel already has an administrator. */
  setupState: (signal?: AbortSignal) => Promise<SetupState>
  /** Creates the first administrator. */
  completeSetup: (request: CompleteSetupRequest, signal?: AbortSignal) => Promise<AuthenticatedUser>
  /** Lists the caller's live sessions. */
  listSessions: (signal?: AbortSignal) => Promise<Session[]>
  /** Ends one of the caller's sessions. */
  revokeSession: (id: string, signal?: AbortSignal) => Promise<boolean>
  /** Starts a two-factor enrolment without enabling anything. */
  beginTwoFactorEnrolment: (signal?: AbortSignal) => Promise<TotpEnrolment>
  /** Completes an enrolment and returns the recovery codes, once. */
  confirmTwoFactorEnrolment: (secret: string, code: string, signal?: AbortSignal) => Promise<RecoveryCodes>
  /** Turns the second factor off. */
  disableTwoFactor: (code: string, signal?: AbortSignal) => Promise<boolean>
}
