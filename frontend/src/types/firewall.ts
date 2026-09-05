/**
 * The transport protocol a port rule applies to, mirroring the backend's `AgentFirewallProtocol`.
 *
 * A union of the two member names the panel serializes, not a numeric enum: the panel-wide
 * `JsonStringEnumConverter` writes enums as their camelCase member name, so `tcp` and `udp` are
 * literally what arrives and what a request has to send back.
 */
export type FirewallProtocol = 'tcp' | 'udp'

/**
 * One port rule, mirroring the backend's `FirewallRuleDto` field-for-field.
 *
 * The same shape names a rule, creates one and removes one, because a rule has no identifier — it
 * IS its port, its protocol and its source range, held by the kernel and not by a row on the
 * server. `AllowPortRequest` and `DenyPortRequest` on the backend carry exactly these three fields
 * for that reason, so a second and a third interface here would be the same shape written three
 * times and three places for it to drift.
 *
 * The listing deliberately does NOT contain the unconditional accepts the agent renders for the
 * host's SSH ports and the panel's own port: those are host facts the module holds and never
 * discloses, which is why this screen cannot tell an SSH rule from any other (see
 * `FirewallLockoutDialog`).
 */
export interface FirewallRule {
  /** The port the rule names, 1-65535. */
  port: number
  /** The transport protocol it applies to. */
  protocol: FirewallProtocol
  /**
   * The source range it is scoped to, as the firewall is actually running it. This is the value a
   * removal has to send back verbatim: a deny whose source range is spelled differently matches
   * nothing and still reports success.
   */
  sourceCidr: string
}

/**
 * Which of the two changes the rules endpoint offers a pending request is: `POST` installs a rule,
 * `DELETE` removes one.
 *
 * It lives here rather than in the component that renders the confirmation, because it is not that
 * component's prop union: the page decides which change is pending, the store sends it, and the
 * dialog only reads it back. A type two of those three would have to import from the third belongs
 * to the contract they share.
 */
export type FirewallRuleChange = 'allow' | 'deny'

/**
 * Why an address was banned, mirroring the backend's `BanReason`.
 *
 * The reason exists on the panel's side only. The agent stores none — the one place a reason could
 * go there is an nftables comment, whose argument `nft` parses in its own grammar — so these rows
 * are the whole of the product's answer to "why is this address cut off", and the reason column is
 * the reason this screen reads the panel's table rather than the kernel's ban set.
 */
export type BanReason = 'manual' | 'bruteForce'

/** One ban still in force, mirroring the backend's `BanDto` field-for-field. */
export interface Ban {
  /** The episode's identity. */
  id: string
  /** The banned address, in the plain form the agent holds it under. */
  ipAddress: string
  /** Why it was banned. */
  reason: BanReason
  /** How many failures the detector counted; zero for a ban an administrator placed by hand. */
  failures: number
  /** When the ban was placed, as an ISO-8601 instant. */
  bannedAt: string
  /** When it runs out as an ISO-8601 instant, or `null` for one that lasts until somebody lifts it. */
  expiresAt: string | null
}

/** Request body for `POST /api/v1/firewall/bans`, mirroring the backend's `BanAddressRequest`. */
export interface BanAddressRequest {
  /** The address to ban. */
  address: string
  /**
   * How long the ban lasts in minutes, or `null` for one that lasts until somebody lifts it.
   *
   * Absent means permanent on purpose, on the backend and here alike: a permanent ban should be
   * something the operator chose by leaving the field empty, never something a zero produced.
   */
  durationMinutes: number | null
}

/** One exempt range, mirroring the backend's `WhitelistEntryDto` field-for-field. */
export interface WhitelistEntry {
  /** The row's identity, and the only identifier a request may name. */
  id: string
  /** The exempt range, exactly as it was written. */
  cidr: string
  /**
   * What the range is, in the administrator's own words — or in the installer's, for the row the
   * panel seeds from the address the server was installed from. The panel writes that note, so it
   * is the only thing distinguishing the seeded row from a hand-written one, and this screen shows
   * it verbatim rather than deciding for itself which row is which.
   */
  note: string
  /** When the row was added, as an ISO-8601 instant. */
  createdAt: string
}

/** Request body for `POST /api/v1/firewall/whitelist`, mirroring the backend's `AddWhitelistEntryRequest`. */
export interface AddWhitelistEntryRequest {
  /** The range to exempt, in CIDR notation. */
  cidr: string
  /** What the range is, for whoever reads it later; at most 200 characters. */
  note: string
}

/**
 * Typed access to the firewall endpoints.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface FirewallApi {
  /**
   * Lists the port rules the firewall is running.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The rules, in the order the panel reports them.
   */
  listRules: (signal?: AbortSignal) => Promise<FirewallRule[]>

  /**
   * Opens a port, scoped to one source range.
   * @param rule The port, protocol and source range to allow.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel installed the rule.
   */
  allowPort: (rule: FirewallRule, signal?: AbortSignal) => Promise<boolean>

  /**
   * Closes a port that was opened, matching the source range the allow was scoped to.
   * @param rule The rule to remove, spelled exactly as the listing reported it.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel removed the rule.
   */
  denyPort: (rule: FirewallRule, signal?: AbortSignal) => Promise<boolean>

  /**
   * Lists the bans still in force, newest first.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The bans, each with the reason it was placed.
   */
  listBans: (signal?: AbortSignal) => Promise<Ban[]>

  /**
   * Bans an address, for a duration or until somebody lifts it.
   * @param request The address to ban and how long for.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel placed the ban.
   */
  banAddress: (request: BanAddressRequest, signal?: AbortSignal) => Promise<boolean>

  /**
   * Lifts every ban in force for one address.
   * @param address The address to let back in.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel lifted the ban.
   */
  unbanAddress: (address: string, signal?: AbortSignal) => Promise<boolean>

  /**
   * Lists the ranges the automatic bans never touch, oldest first.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The exempt ranges.
   */
  listWhitelist: (signal?: AbortSignal) => Promise<WhitelistEntry[]>

  /**
   * Exempts a range from the automatic bans.
   * @param request The range and the note to record.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The row as the panel created it.
   */
  addWhitelistEntry: (request: AddWhitelistEntryRequest, signal?: AbortSignal) => Promise<WhitelistEntry>

  /**
   * Removes an exemption, so the automatic bans may reach the range again.
   * @param id The row to remove.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel removed the row.
   */
  removeWhitelistEntry: (id: string, signal?: AbortSignal) => Promise<boolean>
}
