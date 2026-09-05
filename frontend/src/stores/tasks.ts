import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useTasksApi } from '../composables/apis/useTasksApi'
import { ApiError } from '../composables/useApi'
import type { PanelTask } from '../types/panelTask'

/**
 * How many task streams the panel holds open at once.
 *
 * Each one is a request pinned for the life of the task, and a browser will only give an origin so
 * many. The listing returns up to two hundred rows, so an unbounded "watch everything running"
 * would exhaust the connection pool and take the rest of the panel down with it — the badge would
 * be perfectly accurate on a screen where nothing else could load.
 */
const MAXIMUM_OPEN_WATCHES = 8

/**
 * Owns the panel's background tasks: the listing, the live streams, and therefore the running count
 * the shell header's badge reads.
 *
 * **One store for the page and for the badge, deliberately.** The badge is not a second fetch with
 * its own idea of how many tasks are running; it is a computed over the same array the page renders
 * from, so a frame that arrives on a stream moves both at once and neither can be stale relative to
 * the other. That is also what makes the badge rise without a navigation: nothing about the count
 * depends on which route is mounted.
 *
 * **A 404 from the listing is the surface's own answer, not a fault.** `ListTasksQueryHandler`
 * answers `TaskNotFound` to a customer rather than an empty 200, so that a customer is not told
 * there is an administrator-only feed they were refused. It is stored like any other backend
 * message and rendered verbatim by the page; the badge simply has nothing to count.
 */
export const useTasksStore = defineStore('tasks', () => {
  const api = useTasksApi()

  /** The tasks as last known: what the listing reported, updated in place by every stream frame. */
  const tasks: Ref<PanelTask[]> = ref([])

  /** True while the listing request is in flight. */
  const loading: Ref<boolean> = ref(false)

  /** True once the listing has succeeded at least once. */
  const isLoaded: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the most recent failed read, or `null`. Rendered verbatim; the
   * administrator-only 404 arrives here like any other refusal.
   */
  const errorMessage: Ref<string | null> = ref(null)

  /** The task whose live pane is open, or the empty string when none is. */
  const openTaskId: Ref<string> = ref('')

  /**
   * The abort controller of every open stream, keyed by task id.
   *
   * A plain `Map` rather than reactive state: nothing renders from it, and a controller is an
   * identity to call `abort()` on, not a value to watch. What IS reactive is the effect the stream
   * has — the frames it writes into {@link tasks}.
   */
  const watches = new Map<string, AbortController>()

  /**
   * How many tasks are running right now, and the whole content of the header badge.
   *
   * Computed over {@link tasks}, which is what makes the count answer to the streams as well as to
   * the listing: a frame reporting a task as running raises this the moment it is decoded, wherever
   * the operator happens to be.
   */
  const runningCount: ComputedRef<number> = computed(() => {
    return tasks.value.filter((task) => {
      return task.status === 'running'
    }).length
  })

  /** The task whose live pane is open, or `null`. */
  const openTask: ComputedRef<PanelTask | null> = computed(() => {
    return (
      tasks.value.find((task) => {
        return task.id === openTaskId.value
      }) ?? null
    )
  })

  /** Whether the panel answered successfully and reported no tasks at all. */
  const isEmpty: ComputedRef<boolean> = computed(() => {
    return isLoaded.value && tasks.value.length === 0
  })

  /**
   * Merges one task into the held list, wherever it came from.
   *
   * A task the list does not have is PREPENDED, because the listing is newest-first and anything
   * arriving live is newer than everything in it. A task already held is replaced only when the
   * arrival is at least as new: the module counts a row's changes in `revision` and sends a frame
   * only when it moves, so comparing revisions is how a frame that was slow on the wire is stopped
   * from overwriting a newer snapshot with an older one.
   * @param task The task as the listing or a stream frame reported it.
   * @returns Nothing; the held list updates synchronously.
   */
  const merge = (task: PanelTask): void => {
    const index = tasks.value.findIndex((held) => {
      return held.id === task.id
    })

    if (index === -1) {
      tasks.value = [task, ...tasks.value]
      return
    }

    const held = tasks.value[index]
    if (held !== undefined && task.revision < held.revision) {
      return
    }

    tasks.value = tasks.value.map((candidate, at) => {
      return at === index ? task : candidate
    })
  }

  /**
   * Stops watching one task and releases its connection.
   *
   * Idempotent: a stream that has already ended has removed itself from the map, and asking again
   * is how a component's teardown stays free of knowledge about whether the task finished first.
   * @param id The task to stop watching.
   * @returns Nothing.
   */
  const stopWatching = (id: string): void => {
    const controller = watches.get(id)
    watches.delete(id)
    controller?.abort()
  }

  /**
   * Stops every open stream.
   *
   * Called when the shell tears down. A stream nobody aborts keeps its connection open for as long
   * as the tab lives, and this store outlives every page that used it.
   * @returns Nothing.
   */
  const stopAllWatches = (): void => {
    // Aborted and then cleared in one go, rather than removed one at a time through
    // `stopWatching`: nothing here needs a per-task decision, and mutating the map while iterating
    // it is a hazard this avoids having to reason about at all.
    for (const controller of watches.values()) {
      controller.abort()
    }
    watches.clear()
  }

  /**
   * Watches one task, merging every frame into the held list.
   *
   * Returns immediately: the caller is a component's mount or a row's action, and neither should
   * wait for a task that may run for minutes. Asking to watch a task already being watched does
   * nothing, so a page that re-mounts does not open a second connection to the same stream.
   * @param id The task to watch.
   * @returns Nothing; frames arrive in {@link tasks} as they are decoded.
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
            // Only if it is still OURS: a later `stopWatching` may already have replaced or
            // removed the entry, and deleting unconditionally would drop somebody else's stream
            // out of the map while it was still running.
            if (watches.get(id) === controller) {
              watches.delete(id)
            }
          },
        },
        controller.signal,
      )
      .catch(() => {
        // `watch` reports every ending through `onEnd`, including the failures, so there is
        // nothing here that has not already been handled. The catch exists so an unexpected throw
        // cannot become an unhandled rejection in a panel with `no-floating-promises` on.
        watches.delete(id)
      })
  }

  /**
   * Opens a live stream for each running task in the held list, up to the connection cap.
   *
   * This is what makes the header badge fall on its own: an operator who starts a certificate order
   * and walks away to another screen still sees the count drop when it finishes, because the shell
   * is watching it rather than the page that started it.
   * @returns Nothing.
   */
  const watchRunning = (): void => {
    for (const task of tasks.value) {
      if (task.status === 'running') {
        watch(task.id)
      }
    }
  }

  /**
   * Loads the listing, replacing what is held.
   *
   * The list REPLACES rather than merges, so a task removed by retention leaves the screen. Streams
   * already open are left alone: their frames merge back in, and aborting them here would mean the
   * badge went blind every time the page refreshed.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    // A second request for the same list while one is in flight IS the same request. The shell's
    // badge and the tasks page both want the listing and both mount at once, and which of them
    // gets there first is a detail of template order nobody should have to reason about.
    if (loading.value) {
      return
    }

    loading.value = true
    try {
      tasks.value = await api.list()
      errorMessage.value = null
      isLoaded.value = true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
    } finally {
      loading.value = false
    }
  }

  /**
   * Opens one task's live pane and starts watching it.
   *
   * The stream's first frame is authoritative and always sent, whatever the row's revision — the
   * module says so explicitly — so opening a pane on a task the listing reported some time ago
   * corrects it immediately rather than showing a stale row until the next change.
   * @param id The task to open.
   * @returns Nothing.
   */
  const select = (id: string): void => {
    openTaskId.value = id
    watch(id)
  }

  /**
   * Closes the live pane. The stream stays open: the badge is still counting.
   * @returns Nothing.
   */
  const deselect = (): void => {
    openTaskId.value = ''
  }

  return {
    tasks,
    loading,
    isLoaded,
    errorMessage,
    openTaskId,
    runningCount,
    openTask,
    isEmpty,
    load,
    watch,
    watchRunning,
    stopWatching,
    stopAllWatches,
    select,
    deselect,
  }
})
