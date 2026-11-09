/**
 * The DNS module's server-wide settings and the name server's state, administrators only
 * (`/api/v1/dns-settings` and `/api/v1/dns-server`, issue #178).
 */

/** The values the panel writes into every zone it serves, mirroring the backend's `DnsSettingsDto`. */
export interface DnsSettings {
  /** The host name of the primary name server, the first NS of every zone. */
  primaryNameserver: string
  /** The host name of the secondary name server, or the empty string when there is none. */
  secondaryNameserver: string
  /**
   * The mailbox of the person responsible for the zones in the SOA's dotted form: a dot where the
   * at sign would be, so `hostmaster.example.com` stands for `hostmaster@example.com`.
   */
  hostmasterMailbox: string
  /** The public IPv4 address default records point at, or the empty string. */
  publicIpv4: string
  /** The public IPv6 address default records point at, or the empty string. */
  publicIpv6: string
  /** The TTL records get when none is chosen, in seconds. */
  defaultTtl: number
  /** Whether an administrator has turned the name server on. */
  enabled: boolean
  /** Whether the settings hold everything a zone needs, so zones can be created. */
  complete: boolean
}

/** What `PUT /api/v1/dns-settings` takes: the settings an administrator edits. */
export interface SaveDnsSettingsRequest {
  /** The host name of the primary name server. */
  primaryNameserver: string
  /** The host name of the secondary name server, or the empty string. */
  secondaryNameserver: string
  /** The hostmaster mailbox in the SOA's dotted form (`hostmaster.example.com`). */
  hostmasterMailbox: string
  /** The public IPv4 address, or the empty string. */
  publicIpv4: string
  /** The public IPv6 address, or the empty string. */
  publicIpv6: string
  /** The default TTL in seconds. */
  defaultTtl: number
}

/**
 * Whether the agent saw a safety property of the running name server, mirroring the backend's
 * `DnsPolicyState` (camelCase on the wire). `notObserved` is never read as `inForce`.
 */
export type DnsPolicyState = 'notObserved' | 'inForce' | 'notInForce'

/**
 * Whether this server answers DNS queries, mirroring the backend's `DnsStatusDto`. The name server
 * is off until an administrator turns it on, because turning it on opens port 53 to the internet.
 * "Turned on", "running" and "listening" are three facts: the first is the setting, the other two
 * are read from the host.
 */
export interface DnsServerStatus {
  /** Whether an administrator has turned the name server on. */
  enabled: boolean
  /** Whether the name server process is running. */
  running: boolean
  /** Whether it is listening on port 53. */
  listening: boolean
  /** The name server's version, or the empty string while unknown. */
  version: string
  /** How many zones the name server holds. */
  zoneCount: number
  /** Whether the panel could reach the agent to read the rest. */
  agentReachable: boolean
  /** Whether the running name server was seen to refuse recursion, so it cannot be abused as an open resolver. */
  recursionRefused: DnsPolicyState
  /** Whether the running name server was seen to rate-limit its responses, which blunts amplification attacks. */
  responseRateLimiting: DnsPolicyState
}

/** Typed access to the settings and the name server. Called from Pinia stores only (rules/vue.md). */
export interface DnsSettingsApi {
  /**
   * Reads the settings.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The settings.
   */
  get: (signal?: AbortSignal) => Promise<DnsSettings>

  /**
   * Replaces the settings; every zone is re-rendered with them.
   * @param request The settings an administrator edits.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The settings as saved.
   */
  save: (request: SaveDnsSettingsRequest, signal?: AbortSignal) => Promise<DnsSettings>

  /**
   * Reads the name server's state.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The state.
   */
  server: (signal?: AbortSignal) => Promise<DnsServerStatus>

  /**
   * Turns the name server on, which opens port 53.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The state after turning it on.
   */
  enableServer: (signal?: AbortSignal) => Promise<DnsServerStatus>

  /**
   * Turns the name server off, which closes port 53. Zones are kept.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The state after turning it off.
   */
  disableServer: (signal?: AbortSignal) => Promise<DnsServerStatus>
}
