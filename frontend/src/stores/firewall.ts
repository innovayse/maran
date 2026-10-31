import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useFirewallApi } from '../composables/apis/useFirewallApi'
import { ApiError } from '../composables/useApi'
import type { ListLoad, ListRequest } from '../types/list'
import type { LoadingState } from '../types/loading'
import type {
  AddWhitelistEntryRequest,
  Ban,
  BanAddressRequest,
  FirewallRule,
  WhitelistEntry,
} from '../types/firewall'

/**
 * Owns the three lists the firewall pages are made of — the port rules the host is running, the
 * page of bans in force on screen, and the page of ranges the automatic bans never touch — and every
 * mutation of them. The pages read state from here and call its actions; they never touch the API
 * composable directly (rules/vue.md: "API composables are called from Pinia stores ONLY").
 *
 * **The rules are held whole; the bans and the whitelist are held one server page at a time.** The
 * rules screen asks a safety question of every rule the host runs (would a removal leave a port with
 * no rule at all), so a page of them would answer it wrongly; the panel answers that list unpaged.
 * The bans and the whitelist are read through each page's `useServerTable`, and a mutation is
 * followed by that page reading its list again, never by a local patch: where a row falls is the
 * server's sort, search and paging to say.
 *
 * Error text is never generated here: when the panel rejects a request, its already-localized
 * `title`/`detail` is stored verbatim (rules/vue.md: "the backend owns their text"). The messages
 * are kept in four separate refs rather than one, so a failure is only ever shown beside the list it
 * belongs to: the store outlives each page, and one shared message would follow the operator from
 * the bans page to the rules page and attach a failure to the wrong table.
 *
 * **The rule list is re-read from the panel after every rule change, never patched locally.** The
 * agent re-renders the WHOLE ruleset on each mutation, and a deny whose source range does not match
 * an existing rule removes nothing while still reporting success. A locally-patched list would then
 * show a port as closed while the firewall was still running the rule — the one disagreement this
 * screen must never produce.
 */
export const useFirewallStore = defineStore('firewall', () => {
  const api = useFirewallApi()

  /** The port rules as last reported by the panel; empty before the first successful load. */
  const rules: Ref<FirewallRule[]> = ref([])

  /** The page of bans in force last answered by the panel; empty before the first answer. */
  const bans: Ref<Ban[]> = ref([])

  /** The page of exempt ranges last answered by the panel; empty before the first answer. */
  const whitelist: Ref<WhitelistEntry[]> = ref([])

  /** True while any mutation is in flight, which disables every control that starts another. */
  const acting: Ref<boolean> = ref(false)

  /** Where the read of the rule list stands; every failure sets `errorMessage`, whatever its cause. */
  const loadState: Ref<LoadingState> = ref('loading')

  /**
   * Backend-localized message from the most recent failed read, or `null` when the last one
   * succeeded or none has been attempted. Rendered verbatim.
   */
  const errorMessage: Ref<string | null> = ref(null)

  /** Backend-localized message from the most recent failed rule change, or `null`. */
  const ruleErrorMessage: Ref<string | null> = ref(null)

  /** Backend-localized message from the most recent failed ban or unban, or `null`. */
  const banErrorMessage: Ref<string | null> = ref(null)

  /** Backend-localized message from the most recent failed whitelist change, or `null`. */
  const whitelistErrorMessage: Ref<string | null> = ref(null)

  /**
   * Loads every port rule the host is running, replacing what is held.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loadState.value = 'loading'
    try {
      rules.value = await api.listRules()
      errorMessage.value = null
      loadState.value = 'loaded'
    } catch (error) {
      // A refusal (a customer reaching an administrators-only screen) arrives here exactly like a
      // failure does, and is rendered exactly the same way: as the panel's own message.
      errorMessage.value = error instanceof ApiError ? error.message : null
      loadState.value = 'error'
    }
  }

  /**
   * Re-reads the rule list alone, after a change to it.
   * @returns Resolves once the request has settled; a failure is reported through
   * {@link ruleErrorMessage} rather than thrown, because the change itself already succeeded.
   */
  const reloadRules = async (): Promise<void> => {
    try {
      rules.value = await api.listRules()
    } catch (error) {
      ruleErrorMessage.value = error instanceof ApiError ? error.message : null
    }
  }

  /**
   * Loads one page of the bans in force into {@link bans} and reports how the read ended, for the
   * bans page's `useServerTable`.
   * @param request The page, size, search and sort, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const listBans = async (request: ListRequest): Promise<ListLoad<Ban>> => {
    try {
      const page = await api.listBans(request)
      bans.value = page.items
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Loads one page of the exempt ranges into {@link whitelist} and reports how the read ended, for
   * the whitelist page's `useServerTable`.
   * @param request The page, size, search and sort, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const listWhitelist = async (request: ListRequest): Promise<ListLoad<WhitelistEntry>> => {
    try {
      const page = await api.listWhitelist(request)
      whitelist.value = page.items
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Opens one or more ports, then re-reads what the firewall is actually running.
   *
   * The rules are sent one after another, not in parallel: each call makes the agent re-render the
   * whole ruleset, so a preset that opens two ports has to be two settled changes rather than two
   * races. The first refusal stops the rest — a preset that half-applied and reported success is
   * worse than one that stopped and said why.
   * @param requested The rules to install, in the order they should be sent.
   * @returns True when every rule was installed; the caller reads {@link ruleErrorMessage} for the
   * reason when one was not.
   */
  const allowPorts = async (requested: readonly FirewallRule[]): Promise<boolean> => {
    acting.value = true
    try {
      for (const rule of requested) {
        await api.allowPort(rule)
      }
      ruleErrorMessage.value = null
      return true
    } catch (error) {
      ruleErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      // Always, including after a refusal: an earlier rule in the batch may have been installed,
      // and the list on screen has to show what the host is running rather than what was asked for.
      await reloadRules()
      acting.value = false
    }
  }

  /**
   * Removes one or more rules, then re-reads what the firewall is actually running.
   * @param requested The rules to remove, spelled exactly as the listing reported them.
   * @returns True when every rule was removed; the caller reads {@link ruleErrorMessage} otherwise.
   */
  const denyPorts = async (requested: readonly FirewallRule[]): Promise<boolean> => {
    acting.value = true
    try {
      for (const rule of requested) {
        await api.denyPort(rule)
      }
      ruleErrorMessage.value = null
      return true
    } catch (error) {
      ruleErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      await reloadRules()
      acting.value = false
    }
  }

  /**
   * Bans an address by hand. The page reads its list again on success.
   * @param request The address to ban and how long for.
   * @returns True when the panel placed the ban.
   */
  const banAddress = async (request: BanAddressRequest): Promise<boolean> => {
    acting.value = true
    try {
      await api.banAddress(request)
      banErrorMessage.value = null
      return true
    } catch (error) {
      banErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Lifts every ban in force for one address. The page reads its list again on success rather than
   * filtering locally: one address can hold several episodes, and the panel decides which of them
   * the lift covered.
   * @param address The address to let back in.
   * @returns True when the panel lifted the ban.
   */
  const unbanAddress = async (address: string): Promise<boolean> => {
    acting.value = true
    try {
      await api.unbanAddress(address)
      banErrorMessage.value = null
      return true
    } catch (error) {
      banErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Exempts a range from the automatic bans. The page reads its list again on success: where the
   * new row falls is the server's sort, search and paging to say.
   * @param request The range and the note to record.
   * @returns True when the panel added the row.
   */
  const addWhitelistEntry = async (request: AddWhitelistEntryRequest): Promise<boolean> => {
    acting.value = true
    try {
      await api.addWhitelistEntry(request)
      whitelistErrorMessage.value = null
      return true
    } catch (error) {
      whitelistErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Removes an exemption, so the automatic bans may reach the range again. The page reads its list
   * again on success, since a removal pulls rows up from the next page.
   * @param id The row to remove.
   * @returns True when the panel removed it.
   */
  const removeWhitelistEntry = async (id: string): Promise<boolean> => {
    acting.value = true
    try {
      await api.removeWhitelistEntry(id)
      whitelistErrorMessage.value = null
      return true
    } catch (error) {
      whitelistErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  return {
    rules,
    bans,
    whitelist,
    acting,
    loadState,
    errorMessage,
    ruleErrorMessage,
    banErrorMessage,
    whitelistErrorMessage,
    load,
    listBans,
    listWhitelist,
    allowPorts,
    denyPorts,
    banAddress,
    unbanAddress,
    addWhitelistEntry,
    removeWhitelistEntry,
  }
})
