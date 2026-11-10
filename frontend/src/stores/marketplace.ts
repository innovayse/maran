import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useMarketplaceApi } from '../composables/apis/useMarketplaceApi'
import { useTasksApi } from '../composables/apis/useTasksApi'
import { ApiError } from '../composables/useApi'
import { useLatestRead } from '../composables/useLatestRead'
import type {
  CatalogueQuery,
  InstalledModule,
  InstallModuleRequest,
  MarketplaceModule,
  MarketplaceModuleDetail,
  MarketplaceStatus,
  ModuleOperationAccepted,
  ModuleOperationKind,
  ModuleWaitOutcome,
  ModuleUpdates,
  RemoveModuleRequest,
  RollbackModuleRequest,
  UpdateModuleRequest,
} from '../types/marketplace'
import type { PanelTask } from '../types/panelTask'
import { pause } from '../utils/pause'

/** The task kinds the marketplace starts; an open task of one of them is an operation to resume. */
const OPERATION_KINDS: readonly ModuleOperationKind[] = ['ModuleInstall', 'ModuleUpdate', 'ModuleRemove', 'ModuleRollback']

/** How often an operation's task is re-read while it runs, in milliseconds. */
const TASK_POLL_INTERVAL_MS = 2000

/** The waits between polls of the local panel after a restart, in milliseconds; the last one repeats. */
const RESTART_POLL_DELAYS_MS: readonly number[] = [1000, 2000, 3000, 5000]

/** How long the panel is given to come back after a restart before the wait gives up, in milliseconds. */
const RESTART_WAIT_BUDGET_MS = 120_000

/** How many open tasks of one kind are read when resuming; far more than the one-per-module lock allows. */
const RESUME_PAGE_SIZE = 25

/**
 * Owns the module marketplace for the Marketplace screens (issues #186, #187): the switch, the
 * catalogue, the installed modules, the update check, the operations in flight and the wait for a
 * restarted panel.
 *
 * **The switch gates every read.** While `status.enabled` is not true, the store calls ONLY
 * `GET /api/v1/marketplace/status` (and the local tasks endpoint when resuming, which is not a
 * marketplace call). Every action that would read the catalogue, a module, the installed list or
 * the updates begins with `isOn()` and returns without a request otherwise, so a panel whose
 * administrator never turned the marketplace on makes no marketplace request at all, whatever page
 * code calls. There is no auto-refresh on window focus and no timer outside an operation the
 * administrator started.
 *
 * **Operations are followed here, not on the tasks screen.** An install, update, removal or
 * rollback answers 202 with a task id. The store records it in `operations` (module id to task id),
 * re-reads the task every two seconds through the tasks API (`operationTasks` holds the latest
 * snapshot, final one included) and, when the task ends, refreshes the status and the lists. After
 * a reload `resumeOperations` finds the open tasks of the four kinds again (`GET /api/v1/tasks`
 * filtered by kind and `running`) and follows them; it relies on a marketplace task's `subject`
 * being the module id. No store imports another store.
 *
 * **Errors are the server's text.** A refusal is kept as received in `errorMessage`
 * (`problem.detail`); `errorCode` drives behaviour only (a `MarketplaceDisabled` answer re-reads the
 * status). `unreachable` is true when no answer arrived at all, and the page prints its own
 * sentence for that.
 */
export const useMarketplaceStore = defineStore('marketplace', () => {
  const api = useMarketplaceApi()
  const tasksApi = useTasksApi()
  const catalogueRead = useLatestRead()

  /** The switch and the switch card's facts, or `null` until the first read answered. */
  const status: Ref<MarketplaceStatus | null> = ref(null)

  /** True while the status is being read or the switch is being moved. */
  const statusLoading: Ref<boolean> = ref(false)

  /** The catalogue modules loaded so far, across pages. */
  const items: Ref<MarketplaceModule[]> = ref([])

  /** The search the held catalogue answers; sent again by `loadMore`. */
  const search: Ref<string> = ref('')

  /** The cursor of the next catalogue page, or `null` on the last page or before the first read. */
  const nextCursor: Ref<string | null> = ref(null)

  /** Whether the catalogue's signature was verified; `null` until a catalogue has been read. */
  const verified: Ref<boolean | null> = ref(null)

  /** Why the catalogue is unverified (the panel's sentence), or `null`. */
  const unverifiedMessage: Ref<string | null> = ref(null)

  /** When the panel fetched the catalogue (ISO 8601), or `null`. */
  const fetchedAt: Ref<string | null> = ref(null)

  /** True while the first page of the catalogue is being read. */
  const catalogueLoading: Ref<boolean> = ref(false)

  /** True while a further catalogue page is being read. */
  const loadingMore: Ref<boolean> = ref(false)

  /** The installed modules, or `null` until read. */
  const installed: Ref<InstalledModule[] | null> = ref(null)

  /** True while the installed list is being read. */
  const installedLoading: Ref<boolean> = ref(false)

  /** Module details read so far, by module id. */
  const details: Ref<Record<string, MarketplaceModuleDetail>> = ref({})

  /** True while any module's detail is being read. */
  const detailsLoading: Ref<boolean> = ref(false)

  /** The update check's answer, or `null` until the administrator checked (and again after an operation). */
  const updates: Ref<ModuleUpdates | null> = ref(null)

  /** True while the update check is running. */
  const updatesLoading: Ref<boolean> = ref(false)

  /** Module ids with a request (an operation start, a restart) in flight; the page disables their buttons. */
  const busy: Ref<Record<string, boolean>> = ref({})

  /** Operations in flight: module id to the id of the task that carries it. */
  const operations: Ref<Record<string, string>> = ref({})

  /**
   * The latest snapshot of each followed task, by module id: the progress component reads
   * `percent`, `log` and `failure` here. A snapshot stays after its task ended, so the outcome (a
   * failure's `failure.message`) can be shown until {@link dismissOutcome}.
   */
  const operationTasks: Ref<Record<string, PanelTask>> = ref({})

  /** The panel's sentence for the last refusal, exactly as received, or `null`. */
  const errorMessage: Ref<string | null> = ref(null)

  /** The machine code of the last refusal; behaviour only, never a text key. `null` when none. */
  const errorCode: Ref<string | null> = ref(null)

  /** True when the last request got no answer at all (the page prints its own "unreachable" sentence). */
  const unreachable: Ref<boolean> = ref(false)

  /** True from the moment a restart was accepted until {@link waitForModule} ended. */
  const restarting: Ref<boolean> = ref(false)

  /** Whether `resumeOperations` already ran for this sign-in; it runs once, not on every status read. */
  const resumed: Ref<boolean> = ref(false)

  /**
   * The timers re-reading followed tasks, by module id. A plain `Map`: nothing renders from it, and
   * a timer handle is an identity to clear, not a value to watch.
   */
  const pollTimers = new Map<string, ReturnType<typeof setTimeout>>()

  /** Counts the waits begun and the resets; a wait whose number is no longer current ends as cancelled. */
  let waitGeneration = 0

  /** True when the marketplace is switched on; the only condition under which a marketplace read is made. */
  const enabled: ComputedRef<boolean> = computed(() => {
    return status.value?.enabled === true
  })

  /** Ids of the modules staged and waiting for a restart of the panel. */
  const restartRequiredModules: ComputedRef<string[]> = computed(() => {
    return status.value?.restartRequiredModules ?? []
  })

  /**
   * The guard every marketplace-reading action begins with: the switch is on.
   * @returns True when marketplace requests may be made.
   */
  const isOn = (): boolean => {
    return enabled.value
  }

  /**
   * Copies a record without one key (a new object, so the change is observed).
   * @param record The record.
   * @param key The key to leave out.
   * @returns The record without the key.
   */
  const without = <T>(record: Record<string, T>, key: string): Record<string, T> => {
    return Object.fromEntries(
      Object.entries(record).filter(([name]) => {
        return name !== key
      }),
    )
  }

  /**
   * Forgets everything read from the marketplace. Called when the switch is found off: what was
   * read while it was on must not stay on screen as if still current.
   * @returns Nothing.
   */
  const forgetMarketplaceData = (): void => {
    items.value = []
    nextCursor.value = null
    verified.value = null
    unverifiedMessage.value = null
    fetchedAt.value = null
    installed.value = null
    details.value = {}
    updates.value = null
  }

  /**
   * Keeps a refusal's text and code as received, or notes that no answer arrived.
   * @param error What a request threw.
   * @returns Nothing.
   */
  const readFailure = (error: unknown): void => {
    if (error instanceof ApiError) {
      errorMessage.value = error.message
      errorCode.value = error.code
      unreachable.value = false
    } else {
      errorMessage.value = null
      errorCode.value = null
      unreachable.value = true
    }
  }

  /**
   * Forgets the last refusal; called before a new attempt and when a dialog closes.
   * @returns Nothing.
   */
  const clearError = (): void => {
    errorMessage.value = null
    errorCode.value = null
    unreachable.value = false
  }

  /**
   * Stores a status the panel answered, forgetting the marketplace data when the switch is off.
   * @param next The status.
   * @returns Nothing.
   */
  const applyStatus = (next: MarketplaceStatus): void => {
    status.value = next
    if (!next.enabled) {
      forgetMarketplaceData()
    }
  }

  /**
   * Reads the switch and the switch card's facts. The one read that is always allowed. The first
   * time it finds the marketplace on, it resumes any operation still running.
   * @returns Resolves once the request settled, successfully or not.
   */
  const loadStatus = async (): Promise<void> => {
    statusLoading.value = true
    try {
      applyStatus(await api.status())
      if (enabled.value) {
        await resumeOperations()
      }
    } catch (error) {
      readFailure(error)
    } finally {
      statusLoading.value = false
    }
  }

  /**
   * Keeps a refusal and, when it says the marketplace is off, re-reads the status so the page falls
   * back to the switch card. The code decides behaviour here; the text is the panel's.
   * @param error What a request threw.
   * @returns Resolves once the reaction settled.
   */
  const recordFailure = async (error: unknown): Promise<void> => {
    readFailure(error)
    if (error instanceof ApiError && error.code === 'MarketplaceDisabled') {
      await loadStatus()
    }
  }

  /**
   * Moves the marketplace switch. Turning it on or off asks for the administrator's password again.
   * @param next The new position.
   * @param password The administrator's current password.
   * @returns True when the panel accepted the change; on false see `errorMessage`.
   */
  const setEnabled = async (next: boolean, password: string): Promise<boolean> => {
    clearError()
    statusLoading.value = true
    try {
      applyStatus(await api.setEnabled({ enabled: next, password }))
      return true
    } catch (error) {
      readFailure(error)
      return false
    } finally {
      statusLoading.value = false
    }
  }

  /**
   * Reads the first page of the catalogue (the newest read wins when several are in flight).
   * Does nothing while the marketplace is off.
   * @param query The search and whether to empty the held list first.
   * @returns Resolves once the request settled, successfully or not.
   */
  const loadCatalogue = async (query: CatalogueQuery = {}): Promise<void> => {
    if (!isOn()) {
      return
    }
    if (query.search !== undefined) {
      search.value = query.search
    }
    if (query.reset !== false) {
      items.value = []
      nextCursor.value = null
    }
    clearError()
    const isLatest = catalogueRead.begin()
    catalogueLoading.value = true
    try {
      const page = await api.listCatalogue(null, search.value, null)
      if (isLatest()) {
        items.value = page.items
        nextCursor.value = page.nextCursor
        verified.value = page.verified
        unverifiedMessage.value = page.unverifiedMessage
        fetchedAt.value = page.fetchedAt
      }
    } catch (error) {
      if (isLatest()) {
        await recordFailure(error)
      }
    } finally {
      if (isLatest()) {
        catalogueLoading.value = false
      }
    }
  }

  /**
   * Reads the next catalogue page and appends it. Does nothing while the marketplace is off, on the
   * last page, or while a page is already on its way. A page that arrives after the search changed
   * is dropped.
   * @returns Resolves once the request settled, successfully or not.
   */
  const loadMore = async (): Promise<void> => {
    const cursor = nextCursor.value
    if (!isOn() || cursor === null || loadingMore.value || catalogueLoading.value) {
      return
    }
    clearError()
    const isLatest = catalogueRead.begin()
    loadingMore.value = true
    try {
      const page = await api.listCatalogue(null, search.value, cursor)
      if (isLatest()) {
        items.value = [...items.value, ...page.items]
        nextCursor.value = page.nextCursor
        verified.value = page.verified
        unverifiedMessage.value = page.unverifiedMessage
        fetchedAt.value = page.fetchedAt
      }
    } catch (error) {
      if (isLatest()) {
        await recordFailure(error)
      }
    } finally {
      loadingMore.value = false
    }
  }

  /**
   * Reads the modules installed on this panel. Does nothing while the marketplace is off.
   * @returns Resolves once the request settled, successfully or not.
   */
  const loadInstalled = async (): Promise<void> => {
    if (!isOn()) {
      return
    }
    installedLoading.value = true
    try {
      installed.value = await api.listInstalled()
      await resumeOperations()
    } catch (error) {
      await recordFailure(error)
    } finally {
      installedLoading.value = false
    }
  }

  /**
   * Reads one module's detail into `details`. Does nothing while the marketplace is off.
   * @param id The module's machine id.
   * @returns Resolves once the request settled, successfully or not.
   */
  const loadDetails = async (id: string): Promise<void> => {
    if (!isOn()) {
      return
    }
    clearError()
    detailsLoading.value = true
    try {
      details.value = { ...details.value, [id]: await api.getModule(id) }
    } catch (error) {
      await recordFailure(error)
    } finally {
      detailsLoading.value = false
    }
  }

  /**
   * Checks for newer versions of the installed modules: the one "check for updates" call, made
   * only when the administrator presses the button or opens the tab. Does nothing while the
   * marketplace is off.
   * @returns Resolves once the request settled, successfully or not.
   */
  const checkUpdates = async (): Promise<void> => {
    if (!isOn()) {
      return
    }
    clearError()
    updatesLoading.value = true
    try {
      updates.value = await api.checkUpdates()
    } catch (error) {
      await recordFailure(error)
    } finally {
      updatesLoading.value = false
    }
  }

  /**
   * Re-reads what an ended operation changed: the status (the restart-required list), the
   * installed list, the module's detail and, when a catalogue is held, its first page silently. The
   * update check is forgotten, because its answer is out of date; the administrator checks again.
   * @param id The module the operation was for.
   * @returns Resolves once the reads settled.
   */
  const refreshAfter = async (id: string): Promise<void> => {
    details.value = without(details.value, id)
    updates.value = null
    await loadStatus()
    if (!isOn()) {
      return
    }
    await loadInstalled()
    if (verified.value !== null) {
      await loadCatalogue({ reset: false })
    }
  }

  /**
   * Ends the following of one module's task and refreshes what it changed.
   * @param id The module the operation was for.
   * @returns Resolves once the refresh settled.
   */
  const conclude = async (id: string): Promise<void> => {
    const timer = pollTimers.get(id)
    if (timer !== undefined) {
      clearTimeout(timer)
    }
    pollTimers.delete(id)
    operations.value = without(operations.value, id)
    await refreshAfter(id)
  }

  /**
   * Reads one followed task once, schedules the next read while it runs and concludes the operation
   * when it ended (or when the panel no longer knows the task).
   * @param id The module the operation is for.
   * @param taskId The task that carries it.
   * @returns Resolves once this read settled.
   */
  const pollTask = async (id: string, taskId: string): Promise<void> => {
    // A newer operation on the module, or a stop, makes this poll stale.
    if (operations.value[id] !== taskId) {
      return
    }
    try {
      const task = await tasksApi.get(taskId)
      operationTasks.value = { ...operationTasks.value, [id]: task }
      if (task.finishedAt !== null) {
        await conclude(id)
        return
      }
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        // The task is gone: nothing left to follow, so read the result off the module itself.
        await conclude(id)
        return
      }
      // Any other failure is a missed read, not an ending; the next tick asks again.
    }
    if (operations.value[id] === taskId) {
      pollTimers.set(
        id,
        setTimeout((): void => {
          void pollTask(id, taskId)
        }, TASK_POLL_INTERVAL_MS),
      )
    }
  }

  /**
   * Records an operation the panel accepted and starts following its task. Also what the page
   * calls to adopt a task id it learned elsewhere.
   * @param id The module the operation is for.
   * @param taskId The task that carries it.
   * @returns Nothing; the first read of the task starts at once.
   */
  const trackOperation = (id: string, taskId: string): void => {
    const timer = pollTimers.get(id)
    if (timer !== undefined) {
      clearTimeout(timer)
      pollTimers.delete(id)
    }
    operations.value = { ...operations.value, [id]: taskId }
    operationTasks.value = without(operationTasks.value, id)
    void pollTask(id, taskId)
  }

  /**
   * Finds the marketplace tasks still running (after a reload, or started in another tab) and
   * follows them. Runs once per sign-in; a failed read is skipped, since this is an aid and not the
   * list. Not a marketplace request: it reads the panel's own task list.
   * @returns Resolves once the lookups settled.
   */
  const resumeOperations = async (): Promise<void> => {
    if (resumed.value) {
      return
    }
    resumed.value = true
    const lists = await Promise.all(
      OPERATION_KINDS.map(async (kind): Promise<PanelTask[]> => {
        try {
          const page = await tasksApi.list({
            page: 1,
            pageSize: RESUME_PAGE_SIZE,
            search: '',
            sort: '',
            filters: { kind, status: 'running' },
          })
          return page.items
        } catch {
          return []
        }
      }),
    )
    for (const task of lists.flat()) {
      // A task's subject is the module it acts on; one already followed is left alone.
      if (task.finishedAt === null && operations.value[task.subject] === undefined) {
        operations.value = { ...operations.value, [task.subject]: task.id }
        operationTasks.value = { ...operationTasks.value, [task.subject]: task }
        void pollTask(task.subject, task.id)
      }
    }
  }

  /**
   * Starts one operation on a module: marks the module busy, sends the request and follows the
   * task it answers. A second press while one is in flight does nothing.
   * @param id The module's machine id.
   * @param start Sends the operation's request.
   * @returns True when the panel accepted the operation; on false see `errorMessage`.
   */
  const startOperation = async (id: string, start: () => Promise<ModuleOperationAccepted>): Promise<boolean> => {
    if (!isOn() || busy.value[id] === true) {
      return false
    }
    clearError()
    busy.value = { ...busy.value, [id]: true }
    try {
      const accepted = await start()
      trackOperation(id, accepted.taskId)
      return true
    } catch (error) {
      await recordFailure(error)
      return false
    } finally {
      busy.value = without(busy.value, id)
    }
  }

  /**
   * Installs a module version. The panel checks the password, the acknowledgement and whether the
   * install is allowed; the SPA does not.
   * @param id The module's machine id.
   * @param request The version, the acknowledgement and the re-entered password.
   * @returns True when the install was accepted and its task is being followed.
   */
  const install = (id: string, request: InstallModuleRequest): Promise<boolean> => {
    return startOperation(id, () => {
      return api.install(id, request)
    })
  }

  /**
   * Updates an installed module to a version.
   * @param id The module's machine id.
   * @param request The version, the acknowledgement and the re-entered password.
   * @returns True when the update was accepted and its task is being followed.
   */
  const update = (id: string, request: UpdateModuleRequest): Promise<boolean> => {
    return startOperation(id, () => {
      return api.update(id, request)
    })
  }

  /**
   * Removes an installed module; its data is kept.
   * @param id The module's machine id.
   * @param password The administrator's current password.
   * @returns True when the removal was accepted and its task is being followed.
   */
  const remove = (id: string, password: string): Promise<boolean> => {
    const request: RemoveModuleRequest = { password }
    return startOperation(id, () => {
      return api.remove(id, request)
    })
  }

  /**
   * Rolls an installed module back to its previous version.
   * @param id The module's machine id.
   * @param password The administrator's current password.
   * @returns True when the rollback was accepted and its task is being followed.
   */
  const rollback = (id: string, password: string): Promise<boolean> => {
    const request: RollbackModuleRequest = { password }
    return startOperation(id, () => {
      return api.rollback(id, request)
    })
  }

  /**
   * Restarts the panel so a staged module starts. On acceptance `restarting` is true; the page
   * then calls {@link waitForModule}, which clears it.
   * @param id The module whose staged install the restart is for.
   * @returns True when the panel accepted the restart; on false see `errorMessage`.
   */
  const restart = async (id: string): Promise<boolean> => {
    if (!isOn() || busy.value[id] === true) {
      return false
    }
    clearError()
    busy.value = { ...busy.value, [id]: true }
    try {
      await api.restart(id)
      restarting.value = true
      return true
    } catch (error) {
      await recordFailure(error)
      return false
    } finally {
      busy.value = without(busy.value, id)
    }
  }

  /**
   * Whether the module has reached the state the wait is for, read from the LOCAL panel: the
   * status no longer lists it as waiting for a restart and, when the marketplace is on, the
   * installed list shows the version out of the `installed` lifecycle (running, degraded, failed or
   * removed). Reads only the status and the installed list, never the marketplace's servers.
   * @param id The module's machine id.
   * @param version The version the operation was for.
   * @returns True when the state changed; throws when the panel does not answer.
   */
  const hasChanged = async (id: string, version: string): Promise<boolean> => {
    applyStatus(await api.status())
    if (status.value?.restartRequiredModules.includes(id) === true) {
      return false
    }
    if (!isOn()) {
      return true
    }
    installed.value = await api.listInstalled()
    const entry = installed.value.find((module) => {
      return module.id === id
    })
    return entry !== undefined && entry.version === version && entry.lifecycle !== 'installed'
  }

  /**
   * Waits for the panel to come back after a restart and for the module to leave "restart
   * required": polls the local status (and installed list) with a growing wait, treats an
   * unreachable panel as "not yet", and gives up after two minutes. Clears `restarting` however it
   * ends. A newer wait or {@link reset} cancels this one.
   * @param id The module the restart was for.
   * @param version The version it should now be at.
   * @returns `changed` once the state moved, `timeout` after two minutes, `cancelled` when superseded.
   */
  const waitForModule = async (id: string, version: string): Promise<ModuleWaitOutcome> => {
    waitGeneration += 1
    const mine = waitGeneration
    let waited = 0
    let attempt = 0
    try {
      while (waited < RESTART_WAIT_BUDGET_MS) {
        const delay = RESTART_POLL_DELAYS_MS[Math.min(attempt, RESTART_POLL_DELAYS_MS.length - 1)] ?? 5000
        await pause(delay)
        waited += delay
        attempt += 1
        if (mine !== waitGeneration) {
          return 'cancelled'
        }
        try {
          if (await hasChanged(id, version)) {
            return mine === waitGeneration ? 'changed' : 'cancelled'
          }
        } catch {
          // The panel is down or restarting: not yet. The wait goes on until its budget is spent.
        }
      }
      return 'timeout'
    } finally {
      if (mine === waitGeneration) {
        restarting.value = false
      }
    }
  }

  /**
   * Forgets a finished operation's outcome (its final task snapshot) once the page showed it.
   * @param id The module's machine id.
   * @returns Nothing.
   */
  const dismissOutcome = (id: string): void => {
    if (operations.value[id] === undefined) {
      operationTasks.value = without(operationTasks.value, id)
    }
  }

  /**
   * Stops every timer and cancels a wait, without forgetting what is held; the page calls it when
   * it unmounts. A running operation is picked up again by {@link resumeOperations} on the next visit.
   * @returns Nothing.
   */
  const stopFollowing = (): void => {
    for (const timer of pollTimers.values()) {
      clearTimeout(timer)
    }
    pollTimers.clear()
    waitGeneration += 1
    resumed.value = false
    operations.value = {}
  }

  /**
   * Forgets everything held for the identity that is leaving. Called when the signed-in identity
   * changes, so the next one never sees the previous one's marketplace data.
   * @returns Nothing.
   */
  const reset = (): void => {
    stopFollowing()
    status.value = null
    forgetMarketplaceData()
    search.value = ''
    busy.value = {}
    operationTasks.value = {}
    restarting.value = false
    clearError()
  }

  return {
    status,
    statusLoading,
    items,
    search,
    nextCursor,
    verified,
    unverifiedMessage,
    fetchedAt,
    catalogueLoading,
    loadingMore,
    installed,
    installedLoading,
    details,
    detailsLoading,
    updates,
    updatesLoading,
    busy,
    operations,
    operationTasks,
    errorMessage,
    errorCode,
    unreachable,
    restarting,
    enabled,
    restartRequiredModules,
    loadStatus,
    setEnabled,
    loadCatalogue,
    loadMore,
    loadInstalled,
    loadDetails,
    checkUpdates,
    install,
    update,
    remove,
    rollback,
    restart,
    trackOperation,
    resumeOperations,
    waitForModule,
    dismissOutcome,
    clearError,
    stopFollowing,
    reset,
  }
})
