import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useMyBackupScheduleApi } from '../composables/apis/useMyBackupScheduleApi'
import { ApiError } from '../composables/useApi'
import type { LoadingState } from '../types/loading'
import type { BackupSchedule, SaveMyBackupScheduleRequest } from '../types/backupSchedule'

/**
 * Owns the signed-in customer's own backup schedule. The customer schedule page reads state from
 * here and calls its actions — it never touches the API composable directly (rules/vue.md: "API
 * composables are called from Pinia stores ONLY").
 *
 * Unlike the operator's `backupSchedules` store this one has no scope to switch: the schedule is the
 * caller's, so a read and a write take no account. "Nothing is scheduled" is an ordinary `200` with
 * `schedule: null`, not a failure; every failed request is a real failure, reported in the backend's
 * own words through {@link errorMessage} (rules/vue.md).
 */
export const useMyBackupScheduleStore = defineStore('myBackupSchedule', () => {
  const api = useMyBackupScheduleApi()

  /** The schedule the panel holds for the caller's account, or `null` when it holds none. */
  const schedule: Ref<BackupSchedule | null> = ref(null)

  /** Where the read stands: on its way, arrived, or refused. A save never moves it. */
  const loadState: Ref<LoadingState> = ref('loading')

  /** True while a save is in flight. */
  const saving: Ref<boolean> = ref(false)

  /** True once the last save succeeded, so the screen can confirm it. */
  const saved: Ref<boolean> = ref(false)

  /** Backend-localized message from the last failed read or save, or `null`. Rendered verbatim. */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Machine-stable problem code of the last failure, or the empty string. Held for BEHAVIOUR only
   * (which field the screen marks invalid), never looked up in a translation table.
   */
  const errorCode: Ref<string> = ref('')

  /** Whether the caller's account HAS a schedule; derived so it can never disagree with {@link schedule}. */
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
   * Reads the caller's own schedule, replacing whatever was held.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loadState.value = 'loading'
    schedule.value = null
    errorMessage.value = null
    errorCode.value = ''
    saved.value = false
    try {
      schedule.value = (await api.get()).schedule
      loadState.value = 'loaded'
    } catch (error) {
      remember(error)
      loadState.value = 'error'
    }
  }

  /**
   * Creates or replaces the caller's own schedule and holds what the server stored (which carries the
   * `lastRunAt` and `nextRunAt` the form has no field for).
   * @param request The schedule to store.
   * @returns True when the panel stored it.
   */
  const save = async (request: SaveMyBackupScheduleRequest): Promise<boolean> => {
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
    loadState,
    saving,
    saved,
    errorMessage,
    errorCode,
    load,
    save,
  }
})
