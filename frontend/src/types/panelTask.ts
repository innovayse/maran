/**
 * Where a panel task has got to, mirroring the backend's `PanelTaskStatus`.
 *
 * The wire form is the camelCase member name, not a number: the Host registers
 * `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` on both of its API surfaces, so this union
 * is the contract on the plain endpoints and inside the stream alike.
 */
export type PanelTaskStatus = 'running' | 'completed' | 'failed'

/**
 * Outward view of one background task, mirroring the backend's `PanelTaskDto` field-for-field.
 *
 * One shape for the list, the single read and every frame of the stream, because that is what the
 * module sends: `PanelTaskDto` is the payload of all three.
 *
 * **Every nullable field is read defensively by the API layer**, and that is not belt-and-braces.
 * `TaskStreamWriter` serializes frames with `DefaultIgnoreCondition = WhenWritingNull`, so a
 * streamed frame OMITS `correlationId`, `errorCode` and `finishedAt` where the plain JSON endpoints
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
  /** Everything reported about it so far, capped at the source and marked where it was cut. */
  log: string
  /** The machine-stable code it failed with, or null. Used for behaviour, never as a text key. */
  errorCode: string | null
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
   * Lists the panel's most recent background tasks, newest first.
   *
   * The surface is administrator-only and says so by answering **404, not an empty 200** — a
   * customer is told the feed does not exist for them rather than that it exists and is empty. The
   * store therefore treats a 404 here as "nothing to show", not as an error to shout about.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The tasks, newest first.
   */
  list: (signal?: AbortSignal) => Promise<PanelTask[]>

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
