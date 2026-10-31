import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import { withInstantWindow } from '../../utils/withInstantWindow'
import type { ListRequest, Paged } from '../../types/list'
import {
  TASK_STATUSES,
  type PanelTask,
  type PanelTaskStatus,
  type PanelTaskStreamHandlers,
  type TaskKindChoice,
  type TasksApi,
} from '../../types/panelTask'

/** The endpoint background tasks are listed, read and watched through. */
const TASKS_PATH = '/api/v1/tasks'

/** The list endpoint's parameter for each filter the tasks page reads from its URL. */
const LIST_FILTER_PARAMS: Readonly<Record<string, string>> = {
  account: 'accountId',
  status: 'status',
  kind: 'kind',
  from: 'from',
  to: 'to',
}

/**
 * Builds the tasks API on top of the shared low-level client.
 *
 * Each call is a named `const` arrow function with its own JSDoc rather than an anonymous entry in
 * the returned object: the name is what appears in a stack trace, and the doc block sits next to
 * the call it describes (rules/vue.md).
 * @returns The {@link TasksApi} bound to the panel's tasks endpoints.
 */
export const useTasksApi = (): TasksApi => {
  const api = useApi()

  /**
   * Reads a status off the wire, refusing to invent one this bundle does not know: a pane that says
   * "completed" about an outcome it could not read is worse than one that says the stream ended
   * without naming an outcome.
   * @param named The status text a frame carried, or `undefined` when it carried none.
   * @returns The status, or `null` when the wire named none this panel recognises.
   */
  const readStatus = (named: unknown): PanelTaskStatus | null => {
    return (
      TASK_STATUSES.find((status) => {
        return status === named
      }) ?? null
    )
  }

  /**
   * Normalises one task payload, filling the fields a stream frame may omit.
   *
   * `TaskStreamWriter` serializes with `DefaultIgnoreCondition = WhenWritingNull`, so a frame
   * leaves `correlationId`, `errorCode` and `finishedAt` OUT where the plain endpoints send them as
   * `null`. Both spellings have to reach the store as `null`, or a pane would render `undefined`
   * for a task that simply has not finished.
   * @param payload The decoded JSON of a `task` frame.
   * @returns The task with every nullable field present.
   */
  const readTask = (payload: PanelTask): PanelTask => {
    return {
      ...payload,
      correlationId: payload.correlationId ?? null,
      errorCode: payload.errorCode ?? null,
      finishedAt: payload.finishedAt ?? null,
    }
  }

  /**
   * Reads one page of the tasks the caller may see; the panel searches, filters, sorts and counts.
   * @param request The page, size, search, sort and the account, status, kind, from and to filters.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page.
   */
  const list = async (request: ListRequest, signal?: AbortSignal): Promise<Paged<PanelTask>> => {
    const page = await api.get<Paged<PanelTask>>(
      `${TASKS_PATH}${listQuery(withInstantWindow(request), LIST_FILTER_PARAMS)}`,
      signal,
    )
    return { ...page, items: page.items.map(readTask) }
  }

  /**
   * Lists every running task the caller may see, newest first.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The running tasks.
   */
  const running = async (signal?: AbortSignal): Promise<PanelTask[]> => {
    const tasks = await api.get<PanelTask[]>(`${TASKS_PATH}/running`, signal)
    return tasks.map(readTask)
  }

  /**
   * Lists the distinct kinds of task the caller can see, for the kind filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The kinds with their backend-localized names.
   */
  const kinds = (signal?: AbortSignal): Promise<TaskKindChoice[]> => {
    return api.get<TaskKindChoice[]>(`${TASKS_PATH}/kinds`, signal)
  }

  /**
   * Counts the failed tasks the caller may see, across every page.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns How many failed.
   */
  const failedCount = (signal?: AbortSignal): Promise<number> => {
    return api.get<number>(`${TASKS_PATH}/failed-count`, signal)
  }

  /**
   * Reads one task. A task the caller may not see answers 404, not 403.
   * @param id The task to read.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The task.
   */
  const get = async (id: string, signal?: AbortSignal): Promise<PanelTask> => {
    return readTask(await api.get<PanelTask>(`${TASKS_PATH}/${encodeURIComponent(id)}`, signal))
  }

  /**
   * Watches one task over SSE: a frame each time it changes, then exactly one ending.
   *
   * **This goes through `useApi`'s existing stream helper, unchanged (R9).** The module's
   * `TaskStreamWriter` writes the site-log stream's framing byte for byte — `event:`/`data:` lines,
   * frames separated by a blank line, `: keepalive` comments between them — precisely so that no
   * second client-side parser has to exist. Writing one here would be two decoders of one format,
   * and the second one is always the one that is wrong about chunk boundaries.
   *
   * Exactly one `onEnd` call is made per stream, whatever happened, so a caller never has to infer
   * that a stream is over. An ending is never upgraded: a stream that dropped ends as `null`, the
   * same as one whose ending this bundle could not read.
   * @param id The task to watch.
   * @param handlers Where frames and the ending are delivered.
   * @param signal Abort signal that closes the stream and releases its connection.
   * @returns Resolves once the stream has ended and `onEnd` has been called.
   */
  const watch = async (
    id: string,
    handlers: PanelTaskStreamHandlers,
    signal: AbortSignal,
  ): Promise<void> => {
    // Set the moment the module names an ending, so the natural close that follows it does not
    // overwrite the outcome with "nobody said".
    let ending: PanelTaskStatus | null = null

    try {
      await api.stream(
        `${TASKS_PATH}/${encodeURIComponent(id)}/stream`,
        (event) => {
          if (event.name === 'task') {
            handlers.onTask(readTask(JSON.parse(event.data) as PanelTask))
            return
          }

          if (event.name === 'end') {
            const payload = JSON.parse(event.data) as { status?: unknown }
            ending = readStatus(payload.status)
          }
        },
        signal,
      )
    } catch {
      // A failed or aborted stream is an ending nobody named. The OPERATION carries on regardless —
      // that is the entire reason it was recorded as a task rather than awaited on a request — so
      // this reports only that the panel stopped watching.
      handlers.onEnd(null)
      return
    }

    handlers.onEnd(ending)
  }

  return { list, running, kinds, failedCount, get, watch }
}
