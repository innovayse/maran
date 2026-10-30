import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useBackupSchedulesApi } from '../composables/apis/useBackupSchedulesApi'
import { ApiError } from '../composables/useApi'
import type { BackupSchedule, SaveBackupScheduleRequest } from '../types/backupSchedule'

/**
 * Owns the one backup schedule a screen is looking at: the host-wide policy, or one account's
 * override. The schedule page reads state from here and calls its actions — it never touches the
 * API composable directly (rules/vue.md: "API composables are called from Pinia stores ONLY").
 *
 * **"Nothing is scheduled here" is an answer, not a failure.** The panel invents no default
 * schedule, so a scope nobody has configured is read as `schedule: null` in an ordinary `200` — the
 * first state of every fresh server. Every failed request, whatever its status, is a real failure and
 * is reported through {@link errorMessage} in the backend's own words (rules/vue.md).
 */
export const useBackupSchedulesStore = defineStore('backupSchedules', () => {
  const api = useBackupSchedulesApi()

  /** The schedule the panel holds for the scope last read, or `null` when it holds none. */
  const schedule: Ref<BackupSchedule | null> = ref(null)

  /** True once a read has settled for the current scope, successfully or not. */
  const isLoaded: Ref<boolean> = ref(false)

  /** True while a read is in flight. */
  const loading: Ref<boolean> = ref(false)

  /** True while a save is in flight. */
  const saving: Ref<boolean> = ref(false)

  /** True once the last save succeeded, so the screen can confirm it. */
  const saved: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the last failed read or save, or `null`. Rendered verbatim.
   */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Machine-stable problem code of the last failure, or the empty string.
   *
   * Held for BEHAVIOUR only — which field the screen marks invalid — and never looked up in a
   * translation table (rules/vue.md: the backend owns the text of a server outcome).
   */
  const errorCode: Ref<string> = ref('')

  /**
   * Whether the scope last read or saved HAS a schedule. Derived from {@link schedule} rather than
   * held beside it, so the two can never disagree; a screen pairs it with {@link isLoaded} to tell
   * "nothing is scheduled here" from "not read yet".
   */
  const isConfigured: ComputedRef<boolean> = computed(() => {
    return schedule.value !== null
  })

  /**
   * Records a failure in the backend's own words.
   * @param error The caught error.
   * @returns Nothing; the error state is updated.
   */
  const remember = (error: unknown): void => {
    errorMessage.value = error instanceof ApiError ? error.message : null
    errorCode.value = error instanceof ApiError ? error.code : ''
  }

  /**
   * Reads the schedule for one scope, replacing whatever was held.
   *
   * The held schedule is cleared BEFORE the request rather than after it: the screen fills its form
   * from this value, and leaving the previous scope's schedule in place while the next one loads
   * would show one account's cadence under another account's name.
   * @param accountId The account whose override to read, or `null` for the host-wide policy.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (accountId: string | null): Promise<void> => {
    loading.value = true
    isLoaded.value = false
    schedule.value = null
    errorMessage.value = null
    errorCode.value = ''
    saved.value = false
    try {
      schedule.value = (await api.get(accountId)).schedule
    } catch (error) {
      remember(error)
    } finally {
      isLoaded.value = true
      loading.value = false
    }
  }

  /**
   * Creates or replaces the schedule for one scope and holds what the server stored.
   *
   * What the server answered is held rather than what was sent: the answer carries `lastRunAt`,
   * which the form has no field for and which a save does not change.
   * @param request The schedule to store.
   * @returns True when the panel stored it.
   */
  const save = async (request: SaveBackupScheduleRequest): Promise<boolean> => {
    saving.value = true
    errorMessage.value = null
    errorCode.value = ''
    saved.value = false
    try {
      schedule.value = await api.save(request)
      saved.value = true
      return true
    } catch (error) {
      remember(error)
      return false
    } finally {
      saving.value = false
    }
  }

  return {
    schedule,
    isConfigured,
    isLoaded,
    loading,
    saving,
    saved,
    errorMessage,
    errorCode,
    load,
    save,
  }
})
