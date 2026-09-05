/**
 * The panel's outgoing mail settings, from the angles the settings screen needs
 * them (rules/vue.md "Types" — one domain per file).
 *
 * The read model has no password field, and that is deliberate on the server's
 * side too: `SmtpSettingsDto` cannot carry one, so the screen learns only whether
 * a password is stored. Mirroring that here keeps the guarantee visible in the
 * client's own contract rather than resting on a comment.
 */

/**
 * How the panel protects the connection to the mail server. Mirrors the backend's
 * `SmtpSecurity` enum.
 *
 * `SmtpSettingsDto.Security` carries the real enum, so the host's one panel-wide
 * `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` spells it the same way on
 * both directions of the round trip — the GET answers exactly what the PUT binds.
 */
export type SmtpSecurity = 'none' | 'startTls' | 'implicitTls'

/** The mail settings as `GET /api/v1/monitoring/smtp` reports them. */
export interface SmtpSettings {
  /** Host name or address of the mail server. */
  host: string
  /** TCP port the mail server listens on; `0` on a panel that has never had settings. */
  port: number
  /** How the connection is protected. */
  security: SmtpSecurity
  /** The submission user name, or empty when the server takes no credentials. */
  username: string
  /** Whether a password is stored. Never the value, in any form. */
  hasPassword: boolean
  /** The address the panel's mail is sent from. */
  fromAddress: string
  /** The display name beside the sender address; may be empty. */
  fromName: string
  /** Where alert mail goes. */
  alertRecipient: string
  /** When the settings were last saved, or `null` when the panel has never had any. */
  updatedAt: string | null
}

/**
 * The body of `PUT /api/v1/monitoring/smtp`.
 *
 * `password` is absent when the administrator did not retype one, and the save
 * keeps what is stored; an empty string is a different instruction and clears it.
 * The screen therefore never has to hold — or render — the stored value.
 */
export interface SaveSmtpSettingsRequest {
  /** Host name or address of the mail server. */
  host: string
  /** TCP port the mail server listens on. */
  port: number
  /** How the connection is to be protected. */
  security: SmtpSecurity
  /** The submission user name, or empty when the server takes no credentials. */
  username: string
  /** The new password, or absent to keep the stored one. */
  password?: string
  /** The address the panel's mail is sent from. */
  fromAddress: string
  /** The display name beside the sender address; may be empty. */
  fromName: string
  /** Where alert mail goes. */
  alertRecipient: string
}

/** Public surface of the mail-settings API composable. */
export interface SmtpSettingsApi {
  /** Reads the settings, with a flag in place of the password. */
  get: (signal?: AbortSignal) => Promise<SmtpSettings>
  /** Replaces the settings wholesale. */
  save: (request: SaveSmtpSettingsRequest, signal?: AbortSignal) => Promise<boolean>
  /** Sends one test message to a stated recipient. */
  sendTest: (recipient: string, signal?: AbortSignal) => Promise<boolean>
}
