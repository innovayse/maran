import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useFirewallApi } from '../composables/apis/useFirewallApi'
import { ApiError } from '../composables/useApi'
import type {
  AddWhitelistEntryRequest,
  Ban,
  BanAddressRequest,
  FirewallRule,
  WhitelistEntry,
} from '../types/firewall'

/**
 * Owns the three lists the firewall screen is made of — the port rules the host is running, the
 * bans in force, and the ranges the automatic bans never touch — and every mutation of them. The
 * page reads state from here and calls its actions; it never touches the API composable directly
 * (rules/vue.md: "API composables are called from Pinia stores ONLY").
 *
 * Error text is never generated here: when the panel rejects a request, its already-localized
 * `title`/`detail` is stored verbatim (rules/vue.md: "the backend owns their text"). The messages
 * are kept in four separate refs rather than one, so a rejected whitelist row does not blank the
 * reason a rule change failed — the three sections are on screen at the same time, and one
 * message replacing another would attach a failure to the wrong table.
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

  /** The bans in force as last reported by the panel, newest first. */
  const bans: Ref<Ban[]> = ref([])

  /** The exempt ranges as last reported by the panel, oldest first. */
  const whitelist: Ref<WhitelistEntry[]> = ref([])

  /** True while the initial load of the three lists is in flight. */
  const loading: Ref<boolean> = ref(false)

  /** True while any mutation is in flight, which disables every control that starts another. */
  const acting: Ref<boolean> = ref(false)

  /** True once the three lists have been loaded at least once, successfully. */
  const isLoaded: Ref<boolean> = ref(false)

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
   * Loads the three lists the screen is made of, replacing what is held.
   *
   * The three requests go together rather than one per section: they are one screen, and staggering
   * them would show an operator a rule table beside a bans table describing a different moment.
   * @returns Resolves once every request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loading.value = true
    try {
      const [loadedRules, loadedBans, loadedWhitelist] = await Promise.all([
        api.listRules(),
        api.listBans(),
        api.listWhitelist(),
      ])
      rules.value = loadedRules
      bans.value = loadedBans
      whitelist.value = loadedWhitelist
      errorMessage.value = null
      isLoaded.value = true
    } catch (error) {
      // A refusal (a customer reaching an administrators-only screen) arrives here exactly like a
      // failure does, and is rendered exactly the same way: as the panel's own message.
      errorMessage.value = error instanceof ApiError ? error.message : null
    } finally {
      loading.value = false
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
   * Re-reads the ban list alone, after a change to it.
   * @returns Resolves once the request has settled; a failure is reported through
   * {@link banErrorMessage}.
   */
  const reloadBans = async (): Promise<void> => {
    try {
      bans.value = await api.listBans()
    } catch (error) {
      banErrorMessage.value = error instanceof ApiError ? error.message : null
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
   * Bans an address by hand, then re-reads the bans in force.
   * @param request The address to ban and how long for.
   * @returns True when the panel placed the ban.
   */
  const banAddress = async (request: BanAddressRequest): Promise<boolean> => {
    acting.value = true
    try {
      await api.banAddress(request)
      banErrorMessage.value = null
      await reloadBans()
      return true
    } catch (error) {
      banErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Lifts every ban in force for one address, then re-reads the bans.
   *
   * Re-read rather than filtered locally: one address can hold several episodes, and the panel
   * decides which of them the lift covered.
   * @param address The address to let back in.
   * @returns True when the panel lifted the ban.
   */
  const unbanAddress = async (address: string): Promise<boolean> => {
    acting.value = true
    try {
      await api.unbanAddress(address)
      banErrorMessage.value = null
      await reloadBans()
      return true
    } catch (error) {
      banErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Exempts a range from the automatic bans.
   *
   * The row is taken from the response rather than re-read: the panel answers `201` with the entry
   * it created, including the identity a later removal has to name, so a second request would ask
   * for what is already in hand.
   * @param request The range and the note to record.
   * @returns True when the panel added the row.
   */
  const addWhitelistEntry = async (request: AddWhitelistEntryRequest): Promise<boolean> => {
    acting.value = true
    try {
      whitelist.value = [...whitelist.value, await api.addWhitelistEntry(request)]
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
   * Removes an exemption, so the automatic bans may reach the range again.
   * @param id The row to remove.
   * @returns True when the panel removed it.
   */
  const removeWhitelistEntry = async (id: string): Promise<boolean> => {
    acting.value = true
    try {
      await api.removeWhitelistEntry(id)
      whitelist.value = whitelist.value.filter((entry) => {
        return entry.id !== id
      })
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
    loading,
    acting,
    isLoaded,
    errorMessage,
    ruleErrorMessage,
    banErrorMessage,
    whitelistErrorMessage,
    load,
    allowPorts,
    denyPorts,
    banAddress,
    unbanAddress,
    addWhitelistEntry,
    removeWhitelistEntry,
  }
})
