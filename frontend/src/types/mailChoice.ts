/**
 * One option of a closed set the mail screens select from, as `GET /api/v1/mail/choices` sends it:
 * the wire value the API accepts and the name the panel produced for the caller's language.
 */
export interface MailChoice {
  /** The value the API accepts and serializes for this option, e.g. `forwarder`. */
  value: string
  /** The option's display name in the caller's language, produced by the panel. */
  label: string
}

/**
 * The bounds the mail forms must respect, as the server states them (`MailLimitsDto`). The SPA
 * holds no number of its own: the server keeps validating every value, and these only let a form
 * say so before the round trip.
 */
export interface MailLimits {
  /** The smallest mailbox quota that may be set, in MiB. */
  minimumQuotaMib: number
  /** The largest mailbox quota that may be set, in MiB. */
  maximumQuotaMib: number
  /** The quota a new mailbox starts with, in MiB. */
  defaultQuotaMib: number
  /** The shortest mailbox password, in characters. */
  minimumPasswordLength: number
  /** The most messages one mailbox may send in an hour, at the largest. */
  maximumSendLimitPerHour: number
  /** The send limit a new mailbox starts with, messages per hour. */
  defaultSendLimitPerHour: number
  /** The most targets one alias or forwarder may carry. */
  maximumAliasTargets: number
}

/**
 * The closed sets and bounds the mail screens select from, mirroring `MailChoicesDto`. The lists are
 * advice for the pickers and filters; the server validates every submitted value against the same sets.
 */
export interface MailChoices {
  /** The states a mail domain can be in, in the order the filter offers them. */
  domainStatuses: MailChoice[]
  /** The states a mailbox can be in, in the order the filter offers them. */
  mailboxStatuses: MailChoice[]
  /** The kinds of alias, in the order the form offers them. */
  aliasKinds: MailChoice[]
  /** The bounds of the forms. */
  limits: MailLimits
}

/**
 * Typed access to the mail choices endpoint.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface MailChoicesApi {
  /**
   * Reads the sets and bounds the mail screens need, named in the caller's language.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The choices.
   */
  get: (signal?: AbortSignal) => Promise<MailChoices>
}
