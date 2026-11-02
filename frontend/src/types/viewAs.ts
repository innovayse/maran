import type { AuthenticatedUser } from './auth'

/**
 * The view-as session the current access token belongs to, exactly as the panel reports it
 * (`ViewingAsDto`, issues #317 and #314). The "You are viewing as" banner is drawn from this and
 * from nothing the SPA decided on its own.
 */
export interface ViewingAs {
  /** The account being viewed. */
  accountId: string
  /** The account's name, as the banner shows it. */
  accountName: string
  /** The login being viewed as. */
  username: string
  /** The operator viewing, who Back returns to. */
  operatorUsername: string
  /** When the view-as session ends on its own (ISO 8601). */
  expiresAt: string
}

/** The answer to `GET /api/v1/view-as`: the session behind the current token, or none. */
export interface ViewAsState {
  /** The view-as session, or `null` when the caller is signed in as themselves. */
  viewing: ViewingAs | null
}

/**
 * The answer to `POST /api/v1/view-as`. It carries no refresh token and sets no cookie: the
 * operator's own refresh cookie is what brings them back, and a view-as session cannot be renewed.
 */
export interface ViewAsSession {
  /** The view-as access token, held in memory only, like every access token. */
  accessToken: string
  /** When it stops being accepted; the same instant the view-as session ends. */
  expiresAt: string
  /** The login the token signs in as. */
  user: AuthenticatedUser
  /** What the banner shows. */
  viewing: ViewingAs
}

/** The body of `POST /api/v1/view-as`. */
export interface StartViewAsRequest {
  /** The account to view as. */
  accountId: string
}

/** Public surface of the view-as API composable. */
export interface ViewAsApi {
  /** Reads the view-as session the current token belongs to. */
  current: (signal?: AbortSignal) => Promise<ViewAsState>
  /** Starts viewing as an account's login. */
  start: (request: StartViewAsRequest, signal?: AbortSignal) => Promise<ViewAsSession>
  /** Ends the view-as session the current token belongs to. */
  stop: (signal?: AbortSignal) => Promise<void>
}
