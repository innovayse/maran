import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useBackupSchedulesApi } from '../composables/apis/useBackupSchedulesApi'
import { ApiError } from '../composables/useApi'
import type { BackupSchedule, SaveBackupScheduleRequest } from '../types/backupSchedule'

/** The problem code the panel answers a scope it has never had a schedule for. */
const NOT_CONFIGURED_CODE = 'BackupScheduleNotFound'

/**
 * Owns the one backup schedule a screen is looking at: the host-wide policy, or one account's
 * override. The schedule page reads state from here and calls its actions — it never touches the
 * API composable directly (rules/vue.md: "API composables are called from Pinia stores ONLY").
 *
 * **A 404 is not an error here, and that is the whole reason this store has a third state.** The
 * panel invents no default schedule, so a server nobody has configured answers `404
 * BackupScheduleNotFound` for ever — the ordinary first state of the endpoint. A store that folded
 * that into {@link errorMessage} would make the screen say something is wrong, and a store that
 * folded it into "no schedule loaded yet" would let the screen show an empty form as though a
 * schedule existed. So it is held apart, in {@link isConfigured}, and the screen says which of the
 * two it is looking at.
 *
 * A 404 carrying any OTHER code — `AccountNotFound`, for an account deleted between the list being
 * read and the scope being chosen — is a genuine failure and is reported as one. The decision is
 * made on the code, never on the status alone, and never by looking the code up for text: error
 * text is the backend's and is rendered verbatim (rules/vue.md).
 */
export const useBackupSchedulesStore = defineStore('backupSchedules', () => {
  const api = useBackupSchedulesApi()

  /** The schedule the panel holds for the scope last read, or `null` when it holds none. */
  const schedule: Ref<BackupSchedule | null> = ref(null)

  /**
   * Whether the scope last read HAS a schedule.
   *
   * `false` after a 404, which is a successful answer to "is anything scheduled here" and not a
   * failure. It stays `false` until a read or a save says otherwise, so a screen never has to
   * infer the state from `schedule === null`, which is also true before the first read.
   */
  const isConfigured: Ref<boolean> = ref(false)

  /** True once a read has settled for the current scope, successfully or as a 404. */
  const isLoaded: Ref<boolean> = ref(false)

  /** True while a read is in flight. */
  const loading: Ref<boolean> = ref(false)

  /** True while a save is in flight. */
  const saving: Ref<boolean> = ref(false)

  /** True once the last save succeeded, so the screen can confirm it. */
  const saved: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the last failed read or save, or `null`. Rendered verbatim; a
   * 404 that means "nothing is scheduled here" never reaches it.
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
   * Records a failure, telling the panel's "nothing is scheduled here" apart from a real one.
   * @param error The caught error.
   * @returns Nothing; the error state is updated.
   */
  const remember = (error: unknown): void => {
    const isNotConfigured =
      error instanceof ApiError && error.status === 404 && error.code === NOT_CONFIGURED_CODE
    if (isNotConfigured) {
      // The scope has no schedule. That is an answer, not a fault, and the screen says so.
      schedule.value = null
      isConfigured.value = false
      errorMessage.value = null
      errorCode.value = ''
      return
    }

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
    isConfigured.value = false
    errorMessage.value = null
    errorCode.value = ''
    saved.value = false
    try {
      schedule.value = await api.get(accountId)
      isConfigured.value = true
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
      isConfigured.value = true
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
