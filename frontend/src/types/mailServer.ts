/**
 * What an administrator asked the mail server to be, mirroring the backend's `MailServerSettingsDto`
 * for the fields the SPA reads. The panel's intention, not what the host was observed to do.
 */
export interface MailServerSettings {
  /** Whether the administrator wants the mail daemons running. */
  enabled: boolean
  /** The host name the server presents. */
  hostname: string
}

/**
 * The values a mail server is turned on with when the administrator names none, mirroring the
 * backend's `MailServerStartingValuesDto`. Produced by the panel so the interface holds no figure of
 * its own (rules/vue.md "Data comes from the backend").
 */
export interface MailServerStartingValues {
  /** Whether POP3 and POP3S are served beside IMAP. */
  pop3Enabled: boolean
  /** Whether incoming mail is scanned for viruses. */
  antivirusEnabled: boolean
  /** Whether a forwarder may point outside its account's own mail domains. */
  allowExternalForwarding: boolean
  /** The largest message accepted, in mebibytes. */
  maxMessageMib: number
  /** The spam score from which a message is greylisted. */
  spamGreylistScore: number
  /** The spam score from which a spam header is added. */
  spamAddHeaderScore: number
  /** The spam score from which a message is rejected. */
  spamRejectScore: number
}

/**
 * The mail server as the administrator's status read answers, mirroring the backend's
 * `MailServerStatusDto` for the fields the SPA reads.
 */
export interface MailServerStatus {
  /** What was asked for; `null` on a server where mail was never configured, which is "off". */
  settings: MailServerSettings | null
  /** What a server that is off is turned on with when the administrator names nothing else. */
  startingValues: MailServerStartingValues
}

/**
 * Request body for `POST /api/v1/mail-server/enable`, binding the backend's `EnableMailServerCommand`
 * field-for-field (the caller's address and client are the server's to establish).
 */
export interface EnableMailServerRequest extends MailServerStartingValues {
  /** The host name the server presents; it is also the name its certificate is for. */
  hostname: string
}

/**
 * Typed access to the mail server endpoints. Administrators only: the panel refuses every other role.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface MailServerApi {
  /**
   * Reads what the mail server is configured to do and what it is observed to do.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The status.
   */
  status: (signal?: AbortSignal) => Promise<MailServerStatus>
  /**
   * Turns the mail server on with the given settings.
   * @param request The host name and the starting values.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The status after the daemons were configured.
   */
  enable: (request: EnableMailServerRequest, signal?: AbortSignal) => Promise<MailServerStatus>
}
