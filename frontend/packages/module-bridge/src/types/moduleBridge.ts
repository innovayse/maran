/**
 * The module author's view of bridge protocol v1, the contract between the panel shell and a packaged
 * module's isolated frame. The shell's own copy lives in `frontend/src/types/moduleBridge.ts`; the two
 * describe the same wire and are kept in step by hand, because the frame and the shell share no code.
 */

/** The colour scheme the shell is showing. */
export type BridgeTheme = 'dark' | 'light'

/** The HTTP methods the shell will perform for a frame. */
export type BridgeApiMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'

/** What the shell tells the frame once, when the handshake completes. */
export interface ModuleInit {
  /** The interface language, for example `en`, `ru` or `hy`. */
  locale: string
  /** The colour scheme at the time of the handshake. */
  theme: BridgeTheme
  /** The signed-in user's role name, already localized by the server; display only, never an authorization. */
  roleName: string
  /** The module-relative route the frame was opened for, for example `/hello`. */
  route: string
}

/** The shell's answer to one `request`: the HTTP status and the parsed JSON body (answer or problem document). */
export interface BridgeApiResult {
  /** The HTTP status the API answered with, or 0 when the shell could not reach it. */
  status: number
  /** The parsed JSON body; `null` when the response had none. */
  body: unknown
}

/** A message the shell sends into the frame over the port, after the handshake. */
export type ShellMessage =
  | { v: 1; type: 'api-result'; id: string; status: number; body: unknown }
  | { v: 1; type: 'locale'; locale: string }
  | { v: 1; type: 'theme'; theme: BridgeTheme }

/** Options of `connectToShell`. */
export interface ConnectOptions {
  /** How long to wait for the shell's `init` before giving up, in milliseconds. The shell itself gives up after 10 seconds. */
  timeoutMs?: number
}

/** The live connection to the shell. Nothing else reaches the panel from inside the frame. */
export interface ShellConnection {
  /** What the shell said at the handshake. */
  readonly init: ModuleInit
  /**
   * Asks the shell to call the panel's API with its own session. Rejects, without sending, when the
   * request breaks a limit the shell enforces (path shape, body size, rate).
   */
  request: (method: BridgeApiMethod, path: string, body?: unknown) => Promise<BridgeApiResult>
  /** Asks the shell to navigate to another route of this module; the path must be a declared route. */
  navigate: (path: string) => void
  /** Tells the shell how tall the frame's content is, in CSS pixels. */
  resize: (height: number) => void
  /** Asks the shell to show a plain-text notification. */
  toast: (text: string) => void
  /** Calls the handler when the interface language changes. Returns the function that stops listening. */
  onLocale: (handler: (locale: string) => void) => () => void
  /** Calls the handler when the colour scheme changes. Returns the function that stops listening. */
  onTheme: (handler: (theme: BridgeTheme) => void) => () => void
}
