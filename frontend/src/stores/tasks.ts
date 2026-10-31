import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useTasksApi } from '../composables/apis/useTasksApi'
import { ApiError } from '../composables/useApi'
import type { ListLoad, ListRequest } from '../types/list'
import type { PanelTask, TaskFailureCount, TaskKindChoice } from '../types/panelTask'

/**
 * How many task streams the panel holds open at once.
 *
 * Each one is a request pinned for the life of the task, and a browser will only give an origin so
 * many. An unbounded "watch everything running" would exhaust the connection pool and take the rest
 * of the panel down with it — the badge would be perfectly accurate on a screen where nothing else
 * could load.
 */
const MAXIMUM_OPEN_WATCHES = 8

/**
 * Owns the panel's background tasks: one server page of them for the tasks screen, the running
 * ones the shell header's badge counts, the task whose live pane is open, and the live streams that
 * keep all three current.
 *
 * **Three reads, one source of change.** The page is a server page (`useServerTable`), the running
 * tasks are their own unpaged read (`GET /api/v1/tasks/running`), and the open task is the row the
 * operator chose. Every stream frame merges into all three, so a frame moves the badge, the row and
 * the pane in the same tick and none of them can be stale relative to another — which is also what
 * lets the badge rise without a navigation.
 *
 * **The listing is scoped by the panel, not by this store.** An administrator receives every task;
 * a customer receives the tasks started for their own account, with the log left empty because it
 * is written for the operator (issue #201). The store holds whatever arrives.
 */
export const useTasksStore = defineStore('tasks', () => {
  const api = useTasksApi()

  /** The rows of the page last answered by the panel, updated in place by stream frames. */
  const rows: Ref<PanelTask[]> = ref([])

  /** Every running task the caller may see as last read, updated in place by stream frames. */
  const running: Ref<PanelTask[]> = ref([])

  /** The task whose live pane is open, or `null` when no pane is. */
  const openTask: Ref<PanelTask | null> = ref(null)

  /** The kinds of task the caller can see, for the kind filter; empty until read or when unreadable. */
  const kinds: Ref<TaskKindChoice[]> = ref([])

  /** Where the failed-task count stands, for the notice above the list. */
  const failures: Ref<TaskFailureCount> = ref({ state: 'loading' })

  /**
   * The abort controller of every open stream, keyed by task id.
   *
   * A plain `Map` rather than reactive state: nothing renders from it, and a controller is an
   * identity to call `abort()` on, not a value to watch.
   */
  const watches = new Map<string, AbortController>()

  /**
   * The running-tasks read in flight, or `null` when none is. Not reactive: it only lets a second
   * caller join the request already running instead of starting another.
   */
  let runningRead: Promise<void> | null = null

  /**
   * How many tasks are running right now, and the whole content of the header badge. Computed over
   * {@link running}, which stream frames update, so the count answers to the streams as well as to
   * the read.
   */
  const runningCount: ComputedRef<number> = computed(() => {
    return running.value.filter((task) => {
      return task.status === 'running'
    }).length
  })

  /**
   * Whether an arrival may replace a held copy of the same task. The module counts a row's changes
   * in `revision` and sends a frame only when it moves, so a frame slow on the wire must not
   * overwrite a newer snapshot with an older one.
   * @param held The copy already held.
   * @param arrival The copy that arrived.
   * @returns True when the arrival is at least as new.
   */
  const isCurrent = (held: PanelTask, arrival: PanelTask): boolean => {
    return arrival.revision >= held.revision
  }

  /**
   * Replaces one task in a list by id, when the arrival is at least as new.
   * @param list The list to update.
   * @param task The arrival.
   * @returns The updated list, or `null` when the list does not hold the task.
   */
  const replaceIn = (list: PanelTask[], task: PanelTask): PanelTask[] | null => {
    const index = list.findIndex((held) => {
      return held.id === task.id
    })
    if (index === -1) {
      return null
    }
    const held = list[index]
    if (held !== undefined && !isCurrent(held, task)) {
      return list
    }
    return list.map((candidate, at) => {
      return at === index ? task : candidate
    })
  }

  /**
   * Merges one stream frame into the page, the running tasks and the open pane.
   *
   * The page is only updated in place — where a row falls is the server's to decide, so a task the
   * page does not hold is not inserted. A frame reporting a task the running read did not know as
   * running is PREPENDED there, because that is what raises the badge for work started elsewhere.
   * @param task The task as a stream frame reported it.
   * @returns Nothing; the held state updates synchronously.
   */
  const merge = (task: PanelTask): void => {
    rows.value = replaceIn(rows.value, task) ?? rows.value

    const updated = replaceIn(running.value, task)
    if (updated !== null) {
      running.value = updated
    } else if (task.status === 'running') {
      running.value = [task, ...running.value]
    }

    const open = openTask.value
    if (open !== null && open.id === task.id && isCurrent(open, task)) {
      openTask.value = task
    }
  }

  /**
   * Stops watching one task and releases its connection. Idempotent.
   * @param id The task to stop watching.
   * @returns Nothing.
   */
  const stopWatching = (id: string): void => {
    const controller = watches.get(id)
    watches.delete(id)
    controller?.abort()
  }

  /**
   * Stops every open stream. Called when the shell tears down: this store outlives every page.
   * @returns Nothing.
   */
  const stopAllWatches = (): void => {
    for (const controller of watches.values()) {
      controller.abort()
    }
    watches.clear()
  }

  /**
   * Watches one task, merging every frame. Returns immediately; asking to watch a task already
   * watched does nothing, so a page that re-mounts does not open a second connection.
   * @param id The task to watch.
   * @returns Nothing; frames arrive as they are decoded.
   */
  const watch = (id: string): void => {
    if (watches.has(id) || watches.size >= MAXIMUM_OPEN_WATCHES) {
      return
    }

    const controller = new AbortController()
    watches.set(id, controller)

    void api
      .watch(
        id,
        {
          onTask: merge,
          onEnd: () => {
            // Only if it is still OURS: a later `stopWatching` may already have replaced it.
            if (watches.get(id) === controller) {
              watches.delete(id)
            }
          },
        },
        controller.signal,
      )
      .catch(() => {
        // `watch` reports every ending through `onEnd`; this only keeps an unexpected throw from
        // becoming an unhandled rejection.
        watches.delete(id)
      })
  }

  /**
   * Opens a live stream for each running task, up to the connection cap — what makes the badge fall
   * on its own when work started on another screen finishes.
   * @returns Nothing.
   */
  const watchRunning = (): void => {
    for (const task of running.value) {
      if (task.status === 'running') {
        watch(task.id)
      }
    }
  }

  /**
   * Reads every running task, replacing what is held. A failure leaves nothing to count: the badge
   * is silent rather than shouting on every screen, and the tasks page reports its own read.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadRunning = async (): Promise<void> => {
    // A second request while one is in flight IS the same request: the badge and the tasks page
    // both mount at once.
    if (runningRead !== null) {
      return runningRead
    }

    runningRead = (async (): Promise<void> => {
      try {
        running.value = await api.running()
      } catch {
        running.value = []
      } finally {
        runningRead = null
      }
    })()
    return runningRead
  }

  /**
   * Loads one page of the tasks into {@link rows} and reports how the read ended, for the tasks
   * page's `useServerTable`.
   * @param request The page, size, search, sort and filters, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest): Promise<ListLoad<PanelTask>> => {
    try {
      const page = await api.list(request)
      rows.value = page.items
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Reads the kinds the caller can see into {@link kinds}.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadKinds = async (): Promise<void> => {
    try {
      kinds.value = await api.kinds()
    } catch {
      // The filter is an aid, not the list: an unreadable set leaves it offering "any operation".
      kinds.value = []
    }
  }

  /**
   * Asks the panel how many tasks failed, across every page, into {@link failures}. A failure is
   * recorded as `unavailable`, never as "nothing failed".
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadFailures = async (): Promise<void> => {
    failures.value = { state: 'loading' }
    try {
      failures.value = { state: 'answered', count: await api.failedCount() }
    } catch {
      failures.value = { state: 'unavailable' }
    }
  }

  /**
   * Opens one task's live pane and starts watching it. The stream's first frame is authoritative
   * and always sent, so a row read some time ago is corrected immediately.
   * @param task The task to open.
   * @returns Nothing.
   */
  const select = (task: PanelTask): void => {
    openTask.value = task
    watch(task.id)
  }

  /**
   * Closes the live pane. The stream stays open: the badge is still counting.
   * @returns Nothing.
   */
  const deselect = (): void => {
    openTask.value = null
  }

  return {
    rows,
    running,
    openTask,
    kinds,
    failures,
    runningCount,
    list,
    loadRunning,
    loadKinds,
    loadFailures,
    watch,
    watchRunning,
    stopWatching,
    stopAllWatches,
    select,
    deselect,
  }
})
