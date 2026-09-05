import { useApi } from '../useApi'
import type {
  AddWhitelistEntryRequest,
  Ban,
  BanAddressRequest,
  FirewallApi,
  FirewallRule,
  WhitelistEntry,
} from '../../types/firewall'

/** The endpoint the host's port rules are listed, opened and closed through. */
const RULES_PATH = '/api/v1/firewall/rules'

/** The endpoint the host's address bans are listed, placed and lifted through. */
const BANS_PATH = '/api/v1/firewall/bans'

/** The endpoint the ranges exempt from the automatic bans are managed through. */
const WHITELIST_PATH = '/api/v1/firewall/whitelist'

/**
 * Builds the firewall API on top of the shared low-level client.
 *
 * Each call is a named `const` arrow function with its own JSDoc rather than an anonymous entry in
 * the returned object: the name is what appears in a stack trace, and the doc block sits next to
 * the call it describes (rules/vue.md).
 *
 * **Two of these calls put their arguments in the query string, and both do it because the server
 * asked them to.** A rule has no identifier, so a removal has to name the port, the protocol and
 * the source range — and a request body on DELETE is ignored by enough intermediaries that the rule
 * would sometimes silently survive. An address is dropped from a ban the same way: an IPv6 address
 * is full of colons, and a route segment carrying them is at the mercy of every proxy between the
 * browser and the panel.
 * @returns The {@link FirewallApi} bound to the panel's firewall endpoints.
 */
export const useFirewallApi = (): FirewallApi => {
  const api = useApi()

  /**
   * Spells one rule as the query string `DELETE /api/v1/firewall/rules` binds from.
   *
   * `URLSearchParams` does the encoding rather than a template literal: a source range carries a
   * slash, which has to reach the server as `%2F` or the value binds as something else entirely.
   * @param rule The rule to describe.
   * @returns The encoded query string, without its leading `?`.
   */
  const ruleQuery = (rule: FirewallRule): string => {
    return new URLSearchParams({
      port: String(rule.port),
      protocol: rule.protocol,
      sourceCidr: rule.sourceCidr,
    }).toString()
  }

  /**
   * Lists the port rules the firewall is running.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The rules, in the order the panel reports them.
   */
  const listRules = (signal?: AbortSignal): Promise<FirewallRule[]> => {
    return api.get<FirewallRule[]>(RULES_PATH, signal)
  }

  /**
   * Opens a port, scoped to one source range.
   * @param rule The port, protocol and source range to allow.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel installed the rule.
   */
  const allowPort = (rule: FirewallRule, signal?: AbortSignal): Promise<boolean> => {
    return api.post<boolean>(RULES_PATH, rule, signal)
  }

  /**
   * Closes a port that was opened, matching the source range the allow was scoped to.
   * @param rule The rule to remove, spelled exactly as the listing reported it.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel removed the rule.
   */
  const denyPort = (rule: FirewallRule, signal?: AbortSignal): Promise<boolean> => {
    return api.delete<boolean>(`${RULES_PATH}?${ruleQuery(rule)}`, signal)
  }

  /**
   * Lists the bans still in force, newest first.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The bans, each with the reason it was placed.
   */
  const listBans = (signal?: AbortSignal): Promise<Ban[]> => {
    return api.get<Ban[]>(BANS_PATH, signal)
  }

  /**
   * Bans an address, for a duration or until somebody lifts it.
   * @param request The address to ban and how long for.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel placed the ban.
   */
  const banAddress = (request: BanAddressRequest, signal?: AbortSignal): Promise<boolean> => {
    return api.post<boolean>(BANS_PATH, request, signal)
  }

  /**
   * Lifts every ban in force for one address.
   * @param address The address to let back in.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel lifted the ban.
   */
  const unbanAddress = (address: string, signal?: AbortSignal): Promise<boolean> => {
    const query = new URLSearchParams({ address }).toString()
    return api.delete<boolean>(`${BANS_PATH}?${query}`, signal)
  }

  /**
   * Lists the ranges the automatic bans never touch, oldest first.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The exempt ranges.
   */
  const listWhitelist = (signal?: AbortSignal): Promise<WhitelistEntry[]> => {
    return api.get<WhitelistEntry[]>(WHITELIST_PATH, signal)
  }

  /**
   * Exempts a range from the automatic bans.
   * @param request The range and the note to record.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The row as the panel created it, carrying the identity a later removal names.
   */
  const addWhitelistEntry = (
    request: AddWhitelistEntryRequest,
    signal?: AbortSignal,
  ): Promise<WhitelistEntry> => {
    return api.post<WhitelistEntry>(WHITELIST_PATH, request, signal)
  }

  /**
   * Removes an exemption, so the automatic bans may reach the range again.
   * @param id The row to remove.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel removed the row.
   */
  const removeWhitelistEntry = (id: string, signal?: AbortSignal): Promise<boolean> => {
    return api.delete<boolean>(`${WHITELIST_PATH}/${id}`, signal)
  }

  return {
    listRules,
    allowPort,
    denyPort,
    listBans,
    banAddress,
    unbanAddress,
    listWhitelist,
    addWhitelistEntry,
    removeWhitelistEntry,
  }
}
