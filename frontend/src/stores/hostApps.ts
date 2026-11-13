import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useHostAppsApi } from '../composables/apis/useHostAppsApi'
import { useTasksApi } from '../composables/apis/useTasksApi'
import { useLatestRead } from '../composables/useLatestRead'
import { ApiError } from '../composables/useApi'
import type {
  HostApp,
  HostAppInUse,
  HostAppOperationAccepted,
  HostAppServiceAction,
  HostNodejsUnavailable,
  InstallableHostApp,
} from '../types/hostApp'
import type { ListLoad, ListRequest } from '../types/list'
import type { LoadingState } from '../types/loading'
import type { PanelTask } from '../types/panelTask'
import { hostAppInUseSites } from '../utils/hostAppInUseSites'
import { hostAppRunsOperation } from '../utils/hostAppRunsOperation'
import { withoutKey } from '../utils/withoutKey'

/** The problem code the panel refuses an uninstall with while sites still use the app (`apps.inUse`). */
const IN_USE_CODE = 'AppsInUse'

/** How often a followed install or removal task is re-read while it runs, in milliseconds. */
const TASK_POLL_INTERVAL_MS = 2000

/**
 * Owns the host's apps: one server page of the installed ones for the app store page, the catalogue
 * of what the package sources can install, the service controls, and the installs and removals the
 * panel runs as recorded tasks.
 *
 * **Operations are followed here.** An install or removal answers 202 with a task id. The store
 * records it in {@link operations} (app id to task id), re-reads the task every two seconds through
 * the tasks API and keeps the latest snapshot in {@link operationTasks}, final one included, the way
 * the marketplace follows its own operations; a screen reads progress from there and, when it also
 * holds the shell's running-tasks stream, may show a newer frame of the same task.
 *
 * Error text is never generated here: the panel's already-localized `title`/`detail` is stored
 * verbatim (rules/vue.md). A refusal because sites still use the app is kept apart as
 * {@link inUse}, with the domains the panel lists, because the screen shows them as a list.
 */
export const useHostAppsStore = defineStore('hostApps', () => {
  const api = useHostAppsApi()
  const tasksApi = useTasksApi()
  const latestRead = useLatestRead()

  /** The rows of the installed-apps page last answered by the panel; empty before the first answer. */
  const apps: Ref<HostApp[]> = ref([])

  /** The apps the package sources can install, as last read; empty before the first answer. */
  const installable: Ref<InstallableHostApp[]> = ref([])

  /** Why no Node.js app is among {@link installable}, or `null` when nothing is missing for a reason. */
  const nodejsUnavailable: Ref<HostNodejsUnavailable | null> = ref(null)

  /** The reason in {@link nodejsUnavailable} as the panel's sentence in the user's language, or `null`. */
  const nodejsUnavailableMessage: Ref<string | null> = ref(null)

  /** Where the read of {@link installable} stands. */
  const installableState: Ref<LoadingState> = ref('loading')

  /** The panel's message for the last failed read of {@link installable}, or `null`. */
  const installableErrorMessage: Ref<string | null> = ref(null)

  /** App id to the id of the task carrying its install or removal; an app is absent when none runs. */
  const operations: Ref<Record<string, string>> = ref({})

  /**
   * The latest snapshot of each followed task, by app id. A snapshot stays after its task ended, so
   * the outcome (and a failure's sentence) can still be shown; starting another operation on the
   * app replaces it.
   */
  const operationTasks: Ref<Record<string, PanelTask>> = ref({})

  /** The app a service control is in flight for, or `null`. */
  const controlling: Ref<string | null> = ref(null)

  /** The panel's message for the last refused install, removal or service control, or `null`. */
  const actionErrorMessage: Ref<string | null> = ref(null)

  /** The last removal the panel refused because sites use the app, or `null`. */
  const inUse: Ref<HostAppInUse | null> = ref(null)

  /**
   * The ids of the tasks the tasks API reported finished or no longer knows. A row that still names one
   * of them (the panel died before it settled the row) is not followed again: following it would end at
   * once, reload the table, and meet the same row, without end. A plain `Set`: nothing renders from it.
   */
  const endedTasks = new Set<string>()

  /** The timers re-reading followed tasks, by app id. A plain `Map`: nothing renders from it. */
  const pollTimers = new Map<string, ReturnType<typeof setTimeout>>()

  /**
   * Reads what the package sources can install into {@link installable}.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadInstallable = async (): Promise<void> => {
    installableState.value = 'loading'
    try {
      const answer = await api.listInstallable()
      installable.value = answer.apps
      nodejsUnavailable.value = answer.nodejsUnavailable
      nodejsUnavailableMessage.value = answer.nodejsUnavailableName
      installableErrorMessage.value = null
      installableState.value = 'loaded'
    } catch (error) {
      installable.value = []
      nodejsUnavailable.value = null
      nodejsUnavailableMessage.value = null
      installableErrorMessage.value = error instanceof ApiError ? error.message : null
      installableState.value = 'error'
    }
  }

  /**
   * Ends the following of one app's task.
   * @param id The app the operation was for.
   * @returns Nothing; the snapshot of the task is kept.
   */
  const stopFollowing = (id: string): void => {
    const timer = pollTimers.get(id)
    if (timer !== undefined) {
      clearTimeout(timer)
    }
    pollTimers.delete(id)
    operations.value = withoutKey(operations.value, id)
  }

  /**
   * Reads one followed task once and schedules the next read while it runs. A task the panel no
   * longer knows ends the following; the caller reads the result off the apps themselves.
   * @param id The app the operation is for.
   * @param taskId The task that carries it.
   * @returns Resolves once this read settled.
   */
  const pollTask = async (id: string, taskId: string): Promise<void> => {
    // A newer operation on the app, or a stop, makes this poll stale.
    if (operations.value[id] !== taskId) {
      return
    }
    try {
      const task = await tasksApi.get(taskId)
      operationTasks.value = { ...operationTasks.value, [id]: task }
      if (task.finishedAt !== null) {
        endedTasks.add(taskId)
        stopFollowing(id)
        return
      }
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        endedTasks.add(taskId)
        stopFollowing(id)
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
   * Records an operation the panel accepted and starts following its task.
   * @param id The app the operation is for.
   * @param accepted What the panel answered.
   * @returns Nothing; the first read of the task starts at once.
   */
  const follow = (id: string, accepted: HostAppOperationAccepted): void => {
    const timer = pollTimers.get(id)
    if (timer !== undefined) {
      clearTimeout(timer)
      pollTimers.delete(id)
    }
    operationTasks.value = withoutKey(operationTasks.value, id)
    operations.value = { ...operations.value, [id]: accepted.taskId }
    void pollTask(id, accepted.taskId)
  }

  /**
   * Keeps a refusal's message, and the using sites when the refusal is the in-use one.
   * @param id The app the action was for.
   * @param error What the call threw.
   * @returns Nothing.
   */
  const keepRefusal = (id: string, error: unknown): void => {
    if (error instanceof ApiError && error.code === IN_USE_CODE) {
      inUse.value = { appId: id, message: error.message, ...hostAppInUseSites(error.extensions) }
      actionErrorMessage.value = null
      return
    }
    actionErrorMessage.value = error instanceof ApiError ? error.message : null
  }

  /**
   * Loads one page of the installed apps into {@link apps} and reports how the read ended, for the
   * app store page's `useServerTable`.
   * @param request The page, size, search, sort and filters, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest): Promise<ListLoad<HostApp>> => {
    const isLatest = latestRead.begin()
    try {
      const page = await api.list(request)
      // A slower, older read must not replace the rows of a newer one.
      if (isLatest()) {
        apps.value = page.items
        // A reload while an install or removal runs must not lose it: the row names its task. Only a
        // row that is installing or being removed is followed; a settled one carries no task to watch,
        // and neither does one whose task the tasks API already reported finished or unknown.
        for (const app of page.items) {
          if (hostAppRunsOperation(app) && operations.value[app.appId] === undefined && !endedTasks.has(app.taskId)) {
            follow(app.appId, { taskId: app.taskId })
          }
        }
      }
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Starts an install and follows its task.
   * @param id The catalogue id to install.
   * @returns `true` when the panel accepted the install.
   */
  const install = async (id: string): Promise<boolean> => {
    actionErrorMessage.value = null
    inUse.value = null
    try {
      follow(id, await api.install(id))
      return true
    } catch (error) {
      keepRefusal(id, error)
      return false
    }
  }

  /**
   * Starts a removal and follows its task. A refusal because sites use the app is kept as {@link inUse}.
   * @param id The catalogue id to remove.
   * @returns `true` when the panel accepted the removal.
   */
  const uninstall = async (id: string): Promise<boolean> => {
    actionErrorMessage.value = null
    inUse.value = null
    try {
      follow(id, await api.uninstall(id))
      return true
    } catch (error) {
      keepRefusal(id, error)
      return false
    }
  }

  /**
   * Asks an app's service to start, stop, restart or reload.
   * @param id The catalogue id.
   * @param action What to ask the service to do.
   * @returns `true` when the panel did it.
   */
  const control = async (id: string, action: HostAppServiceAction): Promise<boolean> => {
    controlling.value = id
    actionErrorMessage.value = null
    inUse.value = null
    try {
      await api.control(id, action)
      return true
    } catch (error) {
      keepRefusal(id, error)
      return false
    } finally {
      controlling.value = null
    }
  }

  /**
   * Forgets the last action's refusal, so a message does not outlive the screen that showed it.
   * @returns Nothing.
   */
  const clearRefusals = (): void => {
    actionErrorMessage.value = null
    inUse.value = null
  }

  /**
   * Stops every followed task, as leaving the screen does; the snapshots stay.
   * @returns Nothing.
   */
  const stopAll = (): void => {
    for (const timer of pollTimers.values()) {
      clearTimeout(timer)
    }
    pollTimers.clear()
    operations.value = {}
  }

  return {
    apps,
    installable,
    nodejsUnavailable,
    nodejsUnavailableMessage,
    installableState,
    installableErrorMessage,
    operations,
    operationTasks,
    controlling,
    actionErrorMessage,
    inUse,
    list,
    loadInstallable,
    install,
    uninstall,
    control,
    clearRefusals,
    stopAll,
  }
})
