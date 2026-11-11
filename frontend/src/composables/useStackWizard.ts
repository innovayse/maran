import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useHostAppsStore } from '../stores/hostApps'
import { useStackStore } from '../stores/stack'
import { useTasksStore } from '../stores/tasks'
import type { InstallableHostApp } from '../types/hostApp'
import type { ListRequest } from '../types/list'
import type { PanelTask } from '../types/panelTask'
import { hostAppFamily } from '../utils/hostAppFamily'

/** The wizard's steps, in the order they are walked. */
export type StackWizardStep = 'webServer' | 'runtimes' | 'review' | 'install'

/** The steps in order, so "next" and "back" never hold a position of their own. */
export const STACK_WIZARD_STEPS: readonly StackWizardStep[] = ['webServer', 'runtimes', 'review', 'install']

/** The read that finds the installs still running: the panel's rows that are installing (a handful at most). */
const RUNNING_INSTALLS: ListRequest = {
  page: 1,
  pageSize: 100,
  search: '',
  sort: '',
  filters: { state: 'installing' },
}

/** Where one app of the install queue stands. */
export type StackQueueState = 'waiting' | 'running' | 'done' | 'failed'

/** One app of the install queue. */
export interface StackQueueItem {
  /** The catalogue id. */
  id: string
  /** The app as a reader reads it, taken from the catalogue at the time it was chosen. */
  name: string
  /** Whether the app is the site web server, whose install decides whether the wizard may end. */
  isWebServer: boolean
  /** Where it stands. */
  state: StackQueueState
}

/** What {@link useStackWizard} hands to the wizard screen. */
export interface StackWizard {
  /** The step on screen. */
  step: Ref<StackWizardStep>
  /** The chosen site web server's id, or the empty string before a choice. */
  webServer: Ref<string>
  /** The chosen PHP runtimes' ids. */
  phpIds: Ref<string[]>
  /** The chosen Node.js runtimes' ids. */
  nodeIds: Ref<string[]>
  /** The web servers the host can install. */
  webServerChoices: ComputedRef<InstallableHostApp[]>
  /** The PHP runtimes the host can install. */
  phpChoices: ComputedRef<InstallableHostApp[]>
  /** The Node.js runtimes the host can install. */
  nodeChoices: ComputedRef<InstallableHostApp[]>
  /** The chosen apps in install order: the web server first, then the runtimes. */
  chosen: ComputedRef<InstallableHostApp[]>
  /** The queue the install step walks. */
  queue: Ref<StackQueueItem[]>
  /** Whether the queue is being walked right now. */
  installing: Ref<boolean>
  /** Whether every app of the queue has settled (none is waiting or running). */
  settled: ComputedRef<boolean>
  /** Whether the site web server is installed according to the panel, read after the queue settled. */
  webServerReady: Ref<boolean>
  /** Whether the current step lets the person move on. */
  canAdvance: ComputedRef<boolean>
  /** Moves to the next step. */
  next: () => void
  /** Moves to the previous step. */
  back: () => void
  /** Flips one runtime in or out of the choice. */
  toggle: (family: 'php' | 'nodejs', id: string, chosen: boolean) => void
  /** Walks the queue: installs the web server, then each runtime, one at a time. */
  run: () => Promise<void>
  /** Picks up installs the panel is already running (after a reload) and watches them to their end. */
  resume: () => Promise<void>
  /** The progress of one queued app: the newest of the polled snapshot and the live stream's frame. */
  progressOf: (id: string) => PanelTask | null
}

/**
 * The state and the install loop of the first-run stack wizard (issue #517).
 *
 * The person chooses a site web server (required) and any runtimes, reviews them, and the wizard
 * installs them ONE AT A TIME, the web server first: installs share the host's package manager, and
 * the web server is the one that decides whether sites can exist, so it must not wait behind a
 * runtime. A runtime that fails does not stop the others, but a web server that fails stops the
 * wizard from being left (`webServerReady` stays false) until a retry installs it.
 *
 * Progress is the newest of two sources of the same task: the apps store's polled snapshot (always
 * present, and what decides an ending) and the frame of the shell's running-tasks stream (live,
 * between polls). After the queue settles the stack status is read again, because the panel, not
 * this wizard, says whether a web server is installed.
 *
 * The wizard survives a reload: {@link StackWizard.resume} asks the panel which installs are running,
 * follows their tasks and shows the install step, so an administrator held on this screen by the
 * stack guard is never left with an empty choice while the web server installs. The web server
 * choice is every web server the host's sources offer, whether or not the agent reports it
 * installed: the guard keeps the wizard open only while the panel's ledger says the stack is not
 * ready, and an app the host has that the ledger does not count is installed again (the panel
 * settles the row against the host), not hidden.
 * @returns The wizard's state and actions.
 */
export const useStackWizard = (): StackWizard => {
  const appsStore = useHostAppsStore()
  const stackStore = useStackStore()
  const tasksStore = useTasksStore()

  const step: Ref<StackWizardStep> = ref('webServer')
  const webServer: Ref<string> = ref('')
  const phpIds: Ref<string[]> = ref([])
  const nodeIds: Ref<string[]> = ref([])
  const queue: Ref<StackQueueItem[]> = ref([])
  const installing: Ref<boolean> = ref(false)
  const webServerReady: Ref<boolean> = ref(false)

  /**
   * Picks the installable runtimes of one family that are not installed yet.
   * @param family The family to pick.
   * @returns Those apps, in the order the panel listed them.
   */
  const offered = (family: 'php' | 'nodejs'): InstallableHostApp[] => {
    return appsStore.installable.filter((app) => {
      return !app.installed && hostAppFamily(app.appId) === family
    })
  }

  const webServerChoices: ComputedRef<InstallableHostApp[]> = computed(() => {
    // The agent's `installed` flag is not the stack's readiness (the ledger decides that), so it does
    // not hide a web server here; a web server whose install is running is watched, not offered again.
    return appsStore.installable.filter((app) => {
      return hostAppFamily(app.appId) === 'webServer' && appsStore.operations[app.appId] === undefined
    })
  })

  const phpChoices: ComputedRef<InstallableHostApp[]> = computed(() => {
    return offered('php')
  })

  const nodeChoices: ComputedRef<InstallableHostApp[]> = computed(() => {
    return offered('nodejs')
  })

  const chosen: ComputedRef<InstallableHostApp[]> = computed(() => {
    const wanted = [webServer.value, ...phpIds.value, ...nodeIds.value]
    return wanted.flatMap((id): InstallableHostApp[] => {
      const app = appsStore.installable.find((candidate) => {
        return candidate.appId === id
      })
      return app === undefined ? [] : [app]
    })
  })

  const settled: ComputedRef<boolean> = computed(() => {
    return (
      queue.value.length > 0 &&
      queue.value.every((item) => {
        return item.state === 'done' || item.state === 'failed'
      })
    )
  })

  const canAdvance: ComputedRef<boolean> = computed(() => {
    // The web server is the one required choice; everything after it is optional.
    return step.value !== 'webServer' || webServer.value !== ''
  })

  /**
   * Moves to the next step, never past the last.
   * @returns Nothing.
   */
  const next = (): void => {
    const at = STACK_WIZARD_STEPS.indexOf(step.value)
    step.value = STACK_WIZARD_STEPS[Math.min(at + 1, STACK_WIZARD_STEPS.length - 1)] ?? step.value
  }

  /**
   * Moves to the previous step, never before the first.
   * @returns Nothing.
   */
  const back = (): void => {
    const at = STACK_WIZARD_STEPS.indexOf(step.value)
    step.value = STACK_WIZARD_STEPS[Math.max(at - 1, 0)] ?? step.value
  }

  /**
   * Puts one runtime in or out of the choice.
   * @param family Which list the runtime belongs to.
   * @param id The runtime's catalogue id.
   * @param isChosen Whether it is to be installed.
   * @returns Nothing.
   */
  const toggle = (family: 'php' | 'nodejs', id: string, isChosen: boolean): void => {
    const held = family === 'php' ? phpIds : nodeIds
    const without = held.value.filter((candidate) => {
      return candidate !== id
    })
    held.value = isChosen ? [...without, id] : without
  }

  /**
   * The progress of one queued app.
   * @param id The catalogue id.
   * @returns The newest snapshot of its task, or `null` before the first read of it.
   */
  const progressOf = (id: string): PanelTask | null => {
    const polled = appsStore.operationTasks[id] ?? null
    if (polled === null) {
      return null
    }
    const live = tasksStore.running.find((task) => {
      return task.id === polled.id
    })
    return live !== undefined && live.revision > polled.revision ? live : polled
  }

  /**
   * Waits until the apps store stopped following an app's task, which is how an ending shows.
   * @param id The catalogue id.
   * @returns Resolves once the store no longer follows the app.
   */
  const untilFinished = (id: string): Promise<void> => {
    return new Promise((resolve) => {
      const stop = watch(
        () => {
          return appsStore.operations[id]
        },
        (taskId) => {
          if (taskId === undefined) {
            stop()
            resolve()
          }
        },
        { immediate: true },
      )
    })
  }

  /**
   * Waits for a followed app's task to end and records how it ended.
   * @param item The queue entry whose task is followed.
   * @returns Resolves once the entry settled.
   */
  const settle = async (item: StackQueueItem): Promise<void> => {
    await untilFinished(item.id)
    // A task the panel reports as failed is a failure; a task it no longer knows is judged by the
    // stack status read after the queue, so it counts as done here.
    item.state = appsStore.operationTasks[item.id]?.failure ? 'failed' : 'done'
  }

  /**
   * Installs one queued app and records how it ended.
   * @param item The queue entry.
   * @returns Resolves once the app settled.
   */
  const installOne = async (item: StackQueueItem): Promise<void> => {
    item.state = 'running'
    if (!(await appsStore.install(item.id))) {
      item.state = 'failed'
      return
    }
    await settle(item)
  }

  /**
   * Reads the stack status again once the queue settled, because the panel says whether a web
   * server is installed.
   * @returns Resolves once the status was read.
   */
  const confirmStack = async (): Promise<void> => {
    await stackStore.load(true)
    webServerReady.value = stackStore.isReady === true
  }

  /**
   * Walks the queue. The first run builds it from the choice; a retry keeps the entries that
   * finished and walks the rest again.
   * @returns Resolves once every app settled and the stack status was read again.
   */
  const run = async (): Promise<void> => {
    if (installing.value) {
      return
    }
    installing.value = true
    if (queue.value.length === 0) {
      queue.value = chosen.value.map((app): StackQueueItem => {
        return { id: app.appId, name: app.name, isWebServer: hostAppFamily(app.appId) === 'webServer', state: 'waiting' }
      })
    }
    try {
      for (const item of queue.value) {
        if (item.state !== 'done') {
          await installOne(item)
        }
      }
      await confirmStack()
    } finally {
      installing.value = false
    }
  }

  /**
   * Leaves the install step for the web server choice when the resumed installs settled and the stack
   * is still not ready and nothing in the queue is a failed web server the retry button could run
   * again (the installs picked up were runtimes, or the web server ended without a failure). Without
   * it the administrator, held here by the stack guard, would see an error with no way forward.
   * @returns Nothing.
   */
  const returnToWebServerChoice = (): void => {
    if (webServerReady.value) {
      return
    }
    const retryable = queue.value.some((item) => {
      return item.isWebServer && item.state === 'failed'
    })
    if (!retryable) {
      queue.value = []
      step.value = 'webServer'
    }
  }

  /**
   * Picks up the installs the panel is running, as after a reload: follows their tasks, shows the
   * install step with them as the queue and waits for them to end. Does nothing when none runs or
   * when the wizard is already walking a queue.
   * @returns Resolves once the resumed installs ended and the stack status was read; when the stack is
   * still not ready and there is no failed web server to retry, the wizard is back on its first step.
   */
  const resume = async (): Promise<void> => {
    if (installing.value || queue.value.length > 0) {
      return
    }
    // The read follows every row that names a running task (the apps store's rule).
    const load = await appsStore.list(RUNNING_INSTALLS)
    if (load.kind !== 'loaded' || queue.value.length > 0) {
      return
    }
    const running = load.page.items.filter((app) => {
      return appsStore.operations[app.appId] !== undefined
    })
    if (running.length === 0) {
      return
    }
    installing.value = true
    queue.value = running
      .map((app): StackQueueItem => {
        return { id: app.appId, name: app.name, isWebServer: hostAppFamily(app.appId) === 'webServer', state: 'running' }
      })
      .sort((first, second) => {
        return Number(second.isWebServer) - Number(first.isWebServer)
      })
    step.value = 'install'
    try {
      await Promise.all(queue.value.map(settle))
      await confirmStack()
      returnToWebServerChoice()
    } finally {
      installing.value = false
    }
  }

  return {
    step,
    webServer,
    phpIds,
    nodeIds,
    webServerChoices,
    phpChoices,
    nodeChoices,
    chosen,
    queue,
    installing,
    settled,
    webServerReady,
    canAdvance,
    next,
    back,
    toggle,
    run,
    resume,
    progressOf,
  }
}
