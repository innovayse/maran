import type { ListRequest, Paged } from './list'

/**
 * Where a panel task has got to, mirroring the backend's `PanelTaskStatus`.
 *
 * The wire form is the camelCase member name, not a number: the Host registers
 * `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` on both of its API surfaces, so this union
 * is the contract on the plain endpoints and inside the stream alike.
 */
export type PanelTaskStatus = 'running' | 'completed' | 'failed'

/** Every {@link PanelTaskStatus}, in the order the status filter offers them. */
export const TASK_STATUSES: readonly PanelTaskStatus[] = ['running', 'completed', 'failed']

/**
 * One kind of task the caller can see, as the kind filter offers it: the machine-stable kind the
 * filter sends, and the name an operator reads, localized by the backend.
 */
export interface TaskKindChoice {
  /** The machine-stable kind, sent back as the `kind` filter. */
  kind: string
  /** The kind as an operator reads it, in the request's language. */
  kindName: string
}

/**
 * Where the failed-task count stands on the tasks page: not answered yet, answered, or not
 * answerable. A union rather than a number, so "no task failed" and "the panel could not say" are
 * never the same value.
 */
export type TaskFailureCount =
  | {
      /** The question is on its way. */
      state: 'loading'
    }
  | {
      /** The panel answered. */
      state: 'answered'
      /** How many tasks the caller may see have failed. */
      count: number
    }
  | {
      /** The panel could not be asked or did not answer. */
      state: 'unavailable'
    }

/**
 * Why a task failed, mirroring the backend's `TaskFailureDto`.
 *
 * The sentence is the panel's, already in the operator's language: the backend resolves it from the
 * stored code each time the task is read, so the SPA holds no table of failure sentences (rules/vue.md
 * "Errors from the server — the backend owns their text").
 */
export interface PanelTaskFailure {
  /** The machine-stable code, shown as the detail an operator quotes. Never a text key. */
  code: string
  /** The failure as a sentence, rendered as plain text. */
  message: string
}

/**
 * Outward view of one background task, mirroring the backend's `PanelTaskDto` field-for-field.
 *
 * One shape for the list, the single read and every frame of the stream, because that is what the
 * module sends: `PanelTaskDto` is the payload of all three.
 *
 * **Every nullable field is read defensively by the API layer**, and that is not belt-and-braces.
 * `TaskStreamWriter` serializes frames with `DefaultIgnoreCondition = WhenWritingNull`, so a
 * streamed frame OMITS `correlationId`, `failure` and `finishedAt` where the plain JSON endpoints
 * send them as `null`. Both spellings have to arrive here as `null`.
 */
export interface PanelTask {
  /** The task's identity, and the only identifier a request may name. */
  id: string
  /** What kind of operation it is, as the panel's own `TaskKinds` names it. */
  kind: string
  /** What it acts on — a domain, an account name. Rendered as plain text, never as markup. */
  subject: string
  /** The correlation id of the request that started it, or null. */
  correlationId: string | null
  /** Where it has got to. */
  status: PanelTaskStatus
  /** How far along, 0-100. */
  percent: number
  /**
   * Everything reported about it so far, capped at the source and marked where it was cut, when
   * `logIncluded` is true; empty otherwise.
   */
  log: string
  /**
   * Whether `log` carries the task's log. False for a customer, whose own task's log the panel
   * withholds because it is written for the operator. Its own field so that an empty `log` means
   * only "nothing reported yet", never also "withheld".
   */
  logIncluded: boolean
  /** Why it failed — the panel's sentence and the code beside it — or null while it has not failed. */
  failure: PanelTaskFailure | null
  /** When the operation started, as an ISO-8601 string. */
  startedAt: string
  /** When it reached a final state, or null while it runs. */
  finishedAt: string | null
  /**
   * How many times the row has changed.
   *
   * The module sends a frame only when this moves, so it is what tells a frame already seen from a
   * new one. The store keeps it so a slow frame cannot overwrite a newer snapshot.
   */
  revision: number
}

/**
 * Where the handlers of one watched task's stream deliver what arrives.
 *
 * Two callbacks rather than a returned array, because a stream is not a value that exists at a
 * point in time: the pane and the header badge both have to move as it runs.
 */
export interface PanelTaskStreamHandlers {
  /**
   * Called once per `task` frame, in the order the module sent them.
   * @param task The task exactly as the frame carried it.
   * @returns Nothing.
   */
  onTask: (task: PanelTask) => void

  /**
   * Called exactly once, whatever happened — the module's own ending, an abort, or a transport
   * failure — so a caller never has to infer that a stream is over from frames having stopped.
   * @param status The final status the module named, or `null` when the stream ended without one
   * (a dropped connection, an abort). An ending is never upgraded to a friendlier one.
   * @returns Nothing.
   */
  onEnd: (status: PanelTaskStatus | null) => void
}

/**
 * Typed access to the tasks endpoints.
 *
 * Called from Pinia stores only, never from a component (rules/vue.md).
 */
export interface TasksApi {
  /**
   * Reads one page of the background tasks the caller may see: every task for an administrator,
   * the tasks recorded for their own account for a customer (each with `logIncluded: false`).
   * @param request The page, size, search, sort and the account, status, kind, from and to filters.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page.
   */
  list: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<PanelTask>>

  /**
   * Lists every running task the caller may see, newest first. Unpaged: running work is transient,
   * and this is what the header badge counts and the shell watches.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The running tasks.
   */
  running: (signal?: AbortSignal) => Promise<PanelTask[]>

  /**
   * Lists the distinct kinds of task the caller can see, for the kind filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The kinds with their localized names.
   */
  kinds: (signal?: AbortSignal) => Promise<TaskKindChoice[]>

  /**
   * Counts the failed tasks the caller may see, across every page.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns How many failed.
   */
  failedCount: (signal?: AbortSignal) => Promise<number>

  /**
   * Reads one task. A task the caller may not see answers 404, not 403.
   * @param id The task to read.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The task.
   */
  get: (id: string, signal?: AbortSignal) => Promise<PanelTask>

  /**
   * Watches one task over SSE: a frame each time it changes, then exactly one ending.
   *
   * It goes through the SAME `useApi` stream helper the site-log tail uses (R9). The module's
   * `TaskStreamWriter` writes the site-log stream's framing byte for byte precisely so that no
   * second parser has to exist, and none does.
   * @param id The task to watch.
   * @param handlers Where frames and the ending are delivered.
   * @param signal Abort signal that closes the stream and releases its connection.
   * @returns Resolves once the stream has ended and `onEnd` has been called.
   */
  watch: (
    id: string,
    handlers: PanelTaskStreamHandlers,
    signal: AbortSignal,
  ) => Promise<void>
}
