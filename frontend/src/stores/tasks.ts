import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useTasksApi } from '../composables/apis/useTasksApi'
import { useLatestRead } from '../composables/useLatestRead'
import { ApiError } from '../composables/useApi'
import { useStreamLifecycle } from '../composables/useStreamLifecycle'
import type { ListLoad, ListRequest } from '../types/list'
import type { PanelTask, TaskFailureCount, TaskKindChoice, TaskStatusChoice } from '../types/panelTask'

/**
 * Owns the panel's background tasks: one server page of them for the tasks screen, the running
 * ones the shell header's badge counts, the task whose live pane is open, and the live streams that
 * keep all three current.
 *
 * **Two streams, never more (issue #274).** The running tasks arrive on ONE multiplexed stream the
 * shell opens (`GET /api/v1/tasks/running/stream`, the "tray"), whatever the number of tasks in
 * flight; the open pane watches its own task on the per-task stream, because that task may long
 * since have finished and the tray only carries running work. Every frame of either merges into the
 * page's rows, the running tasks and the pane, so a frame moves the badge, the row and the pane in
 * the same tick — which is also what lets the badge rise without a navigation.
 *
 * **The listing is scoped by the panel, not by this store.** An administrator receives every task;
 * a customer receives the tasks started for their own account, with the log left empty because it
 * is written for the operator (issue #201). The store holds whatever arrives.
 */
export const useTasksStore = defineStore('tasks', () => {
  const api = useTasksApi()
  const latestRead = useLatestRead()

  /**
   * The tray's connection: the ONE running-tasks stream, kept alive by the shared stream lifecycle.
   * A stream that ends or breaks is reopened after a wait that doubles; a refusal (401, 403, 404)
   * closes the tray instead and leaves nothing to count — the badge is silent rather than an error
   * on every screen. The tray stays open in a hidden tab (`pauseWhenHidden: false`): the badge
   * must be right the moment the tab is looked at again.
   */
  const trayStream = useStreamLifecycle({
    pauseWhenHidden: false,
    connect: (connection) => {
      return api.watchRunning(
        {
          onTask: admit,
          onRunning: (ids) => {
            settleRunning(ids)
            // A running set proves the connection works, so the reconnect wait starts over.
            connection.delivered()
          },
        },
        connection.signal,
      )
    },
    onRefused: () => {
      running.value = []
    },
  })

  /** The rows of the page last answered by the panel, updated in place by stream frames. */
  const rows: Ref<PanelTask[]> = ref([])

  /** Every running task the caller may see as last read, updated in place by stream frames. */
  const running: Ref<PanelTask[]> = ref([])

  /** The task whose live pane is open, or `null` when no pane is. */
  const openTask: Ref<PanelTask | null> = ref(null)

  /** The kinds of task the caller can see, for the kind filter; empty until read or when unreadable. */
  const kinds: Ref<TaskKindChoice[]> = ref([])

  /** The statuses a task can be in, for the status filter; empty until read or when unreadable. */
  const statuses: Ref<TaskStatusChoice[]> = ref([])

  /** Where the failed-task count stands, for the notice above the list. */
  const failures: Ref<TaskFailureCount> = ref({ state: 'loading' })

  /**
   * The abort controller of every open per-task stream, keyed by task id — in practice the open
   * pane's alone.
   *
   * A plain `Map` rather than reactive state: nothing renders from it, and a controller is an
   * identity to call `abort()` on, not a value to watch.
   */
  const watches = new Map<string, AbortController>()

  /**
   * Counts the times {@link reset} ran. A read that began under one identity compares it on
   * arrival and drops its answer when another identity has taken over since.
   */
  let generation = 0

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
   * Merges one stream frame into the page, the running tasks and the open pane, in place only.
   *
   * Where a row falls is the server's to decide, so a task the page does not hold is not inserted;
   * and which tasks are running is the tray's to say, so a pane's frame never adds one.
   * @param task The task as a stream frame reported it.
   * @returns Nothing; the held state updates synchronously.
   */
  const merge = (task: PanelTask): void => {
    rows.value = replaceIn(rows.value, task) ?? rows.value
    running.value = replaceIn(running.value, task) ?? running.value

    const open = openTask.value
    if (open !== null && open.id === task.id && isCurrent(open, task)) {
      openTask.value = task
    }
  }

  /**
   * Merges one frame of the tray and, when it reports a running task not yet held, PREPENDS it to
   * the running tasks — that is what raises the badge for work started elsewhere.
   * @param task The task as the tray reported it.
   * @returns Nothing.
   */
  const admit = (task: PanelTask): void => {
    const known = running.value.some((held) => {
      return held.id === task.id
    })
    merge(task)
    if (!known && task.status === 'running') {
      running.value = [task, ...running.value]
    }
  }

  /**
   * Keeps only the running tasks the tray names, which drops one the caller may no longer see and
   * reconciles what was held across a reconnect.
   * @param ids Every running task the caller may see now.
   * @returns Nothing.
   */
  const settleRunning = (ids: string[]): void => {
    const named = new Set(ids)
    running.value = running.value.filter((task) => {
      return named.has(task.id)
    })
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
   * Watches one task on its own stream, merging every frame — what the live pane uses. Returns
   * immediately; asking to watch a task already watched does nothing, so a page that re-mounts does
   * not open a second connection.
   * @param id The task to watch.
   * @returns Nothing; frames arrive as they are decoded.
   */
  const watch = (id: string): void => {
    if (watches.has(id)) {
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
   * Opens the tray: the ONE running-tasks stream, held for as long as the shell is signed in.
   * Idempotent.
   * @returns Nothing; frames arrive as they are decoded.
   */
  const openTray = (): void => {
    trayStream.start()
  }

  /**
   * Closes the tray and cancels a pending reconnect. Called when the shell tears down — on sign-out
   * above all — because this store outlives every page and nothing else would.
   * @returns Nothing.
   */
  const closeTray = (): void => {
    trayStream.stop()
  }

  /**
   * Reopens the tray at once, so its frames are named in the language the reader just chose: the
   * panel resolves each subscriber's language when the stream opens.
   * @returns Nothing.
   */
  const reopenTray = (): void => {
    trayStream.restart()
  }

  /**
   * Loads one page of the tasks into {@link rows} and reports how the read ended, for the tasks
   * page's `useServerTable`.
   * @param request The page, size, search, sort and filters, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest): Promise<ListLoad<PanelTask>> => {
    const isLatest = latestRead.begin()
    try {
      const page = await api.list(request)
      // A slower, older read must not replace the rows of a newer one.
      if (isLatest()) {
        rows.value = page.items
      }
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
   * Reads the statuses a task can be in into {@link statuses}.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadStatuses = async (): Promise<void> => {
    try {
      statuses.value = await api.statuses()
    } catch {
      // The filter is an aid, not the list: an unreadable set leaves it offering "any status".
      statuses.value = []
    }
  }

  /**
   * Asks the panel how many tasks failed, across every page, into {@link failures}. A failure is
   * recorded as `unavailable`, never as "nothing failed".
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadFailures = async (): Promise<void> => {
    const asked = generation
    failures.value = { state: 'loading' }
    try {
      const count = await api.failedCount()
      if (asked === generation) {
        failures.value = { state: 'answered', count }
      }
    } catch {
      // An answer that belongs to an identity that has since left must not colour the new one.
      if (asked === generation) {
        failures.value = { state: 'unavailable' }
      }
    }
  }

  /**
   * Forgets everything held for the identity that is leaving: closes the tray and every per-task
   * stream, and empties the rows, the running tasks, the open pane, the filters' choices and the
   * failed-task count. Called when the signed-in identity changes (a sign-out, a sign-in as someone
   * else, "View as this account" starting or ending), so the next identity never begins with the
   * previous one's badge or has the previous one's stream carried over.
   * @returns Nothing; state is cleared synchronously.
   */
  const reset = (): void => {
    generation += 1
    closeTray()
    for (const controller of watches.values()) {
      controller.abort()
    }
    watches.clear()
    rows.value = []
    running.value = []
    openTask.value = null
    kinds.value = []
    statuses.value = []
    failures.value = { state: 'loading' }
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
   * Closes the live pane and releases its stream; the badge goes on counting from the tray.
   * @returns Nothing.
   */
  const deselect = (): void => {
    const open = openTask.value
    openTask.value = null
    if (open !== null) {
      stopWatching(open.id)
    }
  }

  return {
    rows,
    running,
    openTask,
    kinds,
    statuses,
    failures,
    runningCount,
    list,
    loadKinds,
    loadStatuses,
    loadFailures,
    reset,
    watch,
    stopWatching,
    openTray,
    closeTray,
    reopenTray,
    select,
    deselect,
  }
})
