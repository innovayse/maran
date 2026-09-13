import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useCronApi } from '../composables/apis/useCronApi'
import { ApiError } from '../composables/useApi'
import type { CronEntry, CronEntryOutput, CronSchedule } from '../types/cronEntry'
import type { CronEnvironmentVariable } from '../types/cronEnvironmentVariable'

/**
 * Owns the cron screen's state: the account being looked at, that account's entries, its managed
 * environment assignments, and the reading of one entry's last run.
 *
 * **The account is state here rather than a route parameter**, because it is what every one of the
 * module's calls names. Cron keeps no rows, so an entry id means nothing until it is asked of one
 * account's crontab; a screen that forgot which account it was showing would be asking an
 * unanswerable question rather than a wider one.
 *
 * Error text is never generated here: when the panel rejects a request, its already-localized
 * `title`/`detail` is stored verbatim (rules/vue.md: "the backend owns their text"). The page
 * renders it as plain text.
 *
 * The entries list is replaced from the server after every mutation rather than patched in place.
 * The list is a reading of the crontab, not a memory of what the panel installed — the account can
 * edit that crontab directly — so re-reading is the only thing that keeps the screen honest.
 */
export const useCronStore = defineStore('cron', () => {
  const api = useCronApi()

  /** The account whose crontab is on screen, or the empty string before one is chosen. */
  const accountId: Ref<string> = ref('')

  /** The entries as last reported by the agent; empty before the first successful load. */
  const entries: Ref<CronEntry[]> = ref([])

  /** The managed environment assignments as last reported; empty before the first load. */
  const environment: Ref<CronEnvironmentVariable[]> = ref([])

  /** True while the entries request is in flight. */
  const loading: Ref<boolean> = ref(false)

  /** True while a create or an update is in flight. */
  const saving: Ref<boolean> = ref(false)

  /** True while a switch or a removal is in flight. */
  const acting: Ref<boolean> = ref(false)

  /** True while the environment set is being written. */
  const savingEnvironment: Ref<boolean> = ref(false)

  /** True once the entries of the currently selected account have loaded successfully. */
  const isLoaded: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the most recent failed read, switch or removal, or `null`.
   * Rendered verbatim.
   */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Backend-localized message from the most recent failed create or update, or `null`. Kept apart
   * from {@link errorMessage} so a rejected form does not blank the list's own error.
   */
  const saveErrorMessage: Ref<string | null> = ref(null)

  /** Backend-localized message from the most recent failed environment write, or `null`. */
  const environmentErrorMessage: Ref<string | null> = ref(null)

  /** The entry whose last run is being shown, or the empty string when the dialog is closed. */
  const outputEntryId: Ref<string> = ref('')

  /**
   * The reading of that entry's last run, or `null`.
   *
   * `null` is ambiguous on its own and is disambiguated by {@link outputLoading} and
   * {@link outputEntryId}: the endpoint answers 200 with a null BODY for an entry that has never
   * run, so "no reading" is a real answer the dialog states plainly rather than an empty one it
   * invents.
   */
  const output: Ref<CronEntryOutput | null> = ref(null)

  /** True while the last-run reading is in flight. */
  const outputLoading: Ref<boolean> = ref(false)

  /** Backend-localized message from a failed last-run read, or `null`. */
  const outputErrorMessage: Ref<string | null> = ref(null)

  /** Whether the panel answered successfully and reported no entries at all. */
  const isEmpty: ComputedRef<boolean> = computed(() => {
    return isLoaded.value && entries.value.length === 0
  })

  /** Whether the last-run dialog is open. */
  const isOutputOpen: ComputedRef<boolean> = computed(() => {
    return outputEntryId.value !== ''
  })

  /**
   * Loads the selected account's entries, replacing what is held.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    if (accountId.value === '') {
      return
    }

    loading.value = true
    try {
      entries.value = await api.list(accountId.value)
      errorMessage.value = null
      isLoaded.value = true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
    } finally {
      loading.value = false
    }
  }

  /**
   * Loads the selected account's managed environment assignments.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadEnvironment = async (): Promise<void> => {
    if (accountId.value === '') {
      return
    }

    try {
      environment.value = await api.listEnvironment(accountId.value)
      environmentErrorMessage.value = null
    } catch (error) {
      environmentErrorMessage.value = error instanceof ApiError ? error.message : null
    }
  }

  /**
   * Points the screen at an account and reads everything that belongs to it.
   *
   * Both reads are cleared first, so a slow answer for the previous account cannot land under the
   * new one's heading — which would be one customer's commands shown against another's name.
   * @param id The account to look at.
   * @returns Resolves once both requests have settled.
   */
  const selectAccount = async (id: string): Promise<void> => {
    accountId.value = id
    entries.value = []
    environment.value = []
    isLoaded.value = false
    errorMessage.value = null
    environmentErrorMessage.value = null
    await Promise.all([load(), loadEnvironment()])
  }

  /**
   * Installs a new entry and re-reads the crontab.
   * @param schedule When the entry is to run.
   * @param command The command line to install, verbatim.
   * @returns True when the panel installed it — the caller reads {@link saveErrorMessage} for the
   * reason when it did not.
   */
  const create = async (schedule: CronSchedule, command: string): Promise<boolean> => {
    saving.value = true
    try {
      await api.create({ accountId: accountId.value, schedule, command })
      saveErrorMessage.value = null
      await load()
      return true
    } catch (error) {
      saveErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      saving.value = false
    }
  }

  /**
   * Rewrites an entry's schedule and command, leaving its enablement exactly as it was.
   * @param entryId The entry to rewrite.
   * @param schedule The new schedule.
   * @param command The new command line, verbatim.
   * @returns True when the panel rewrote it.
   */
  const update = async (
    entryId: string,
    schedule: CronSchedule,
    command: string,
  ): Promise<boolean> => {
    saving.value = true
    try {
      await api.update(entryId, { accountId: accountId.value, schedule, command })
      saveErrorMessage.value = null
      await load()
      return true
    } catch (error) {
      saveErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      saving.value = false
    }
  }

  /**
   * Switches an entry on or off. The state is sent explicitly rather than toggled, matching the
   * module: a toggle applied to a row the operator last saw seconds ago switches whatever it finds.
   * @param entryId The entry to switch.
   * @param enabled True installs it as a live crontab line; false comments it out.
   * @returns True when the panel switched it.
   */
  const setEnabled = async (entryId: string, enabled: boolean): Promise<boolean> => {
    acting.value = true
    try {
      await api.setEnabled(entryId, { accountId: accountId.value, enabled })
      errorMessage.value = null
      await load()
      return true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Removes an entry, with the files that held its command and its last run.
   * @param entryId The entry to remove.
   * @returns True when the panel removed it.
   */
  const remove = async (entryId: string): Promise<boolean> => {
    acting.value = true
    try {
      await api.remove(entryId, accountId.value)
      errorMessage.value = null
      await load()
      return true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Replaces the managed environment assignments with exactly the set given.
   *
   * A name absent from the set is REMOVED from the crontab and an empty set clears them all; the
   * screen says so beside the button, because the verb the module chose is the warning.
   * @param variables The complete new set.
   * @returns True when the panel rewrote them.
   */
  const saveEnvironment = async (variables: CronEnvironmentVariable[]): Promise<boolean> => {
    savingEnvironment.value = true
    try {
      await api.setEnvironment({ accountId: accountId.value, variables })
      environmentErrorMessage.value = null
      await loadEnvironment()
      return true
    } catch (error) {
      environmentErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      savingEnvironment.value = false
    }
  }

  /**
   * Opens the last-run dialog for one entry and reads what that run left behind.
   * @param entryId The entry to read.
   * @returns Resolves once the reading has settled, successfully or not.
   */
  const openOutput = async (entryId: string): Promise<void> => {
    outputEntryId.value = entryId
    output.value = null
    outputErrorMessage.value = null
    outputLoading.value = true
    try {
      output.value = await api.getOutput(entryId, accountId.value)
    } catch (error) {
      outputErrorMessage.value = error instanceof ApiError ? error.message : null
    } finally {
      outputLoading.value = false
    }
  }

  /**
   * Closes the last-run dialog and forgets the reading it showed.
   * @returns Nothing.
   */
  const closeOutput = (): void => {
    outputEntryId.value = ''
    output.value = null
    outputErrorMessage.value = null
  }

  return {
    accountId,
    entries,
    environment,
    loading,
    saving,
    acting,
    savingEnvironment,
    isLoaded,
    errorMessage,
    saveErrorMessage,
    environmentErrorMessage,
    outputEntryId,
    output,
    outputLoading,
    outputErrorMessage,
    isEmpty,
    isOutputOpen,
    selectAccount,
    load,
    loadEnvironment,
    create,
    update,
    setEnabled,
    remove,
    saveEnvironment,
    openOutput,
    closeOutput,
  }
})
