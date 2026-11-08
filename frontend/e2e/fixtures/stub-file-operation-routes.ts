import type { Page, Route } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type { FileBatchResult, TrashEntry } from '../../src/types/file'

/** The trash endpoint, with or without a query: the list reads it, the empty call deletes on it. */
const TRASH_PATTERN = /\/api\/v1\/files\/trash(\?.*)?$/

/** Every endpoint that changes files, which the operation stub answers. */
const OPERATION_PATTERN =
  /\/api\/v1\/files\/(entries|rename|move|copy|permissions|trash|trash\/restore|delete|archive|extract)(\?.*)?$/

/** One request the operation stub received. */
export interface RecordedOperation {
  /** The HTTP method. */
  method: string
  /** The path after `/api/v1/files/`, e.g. `rename` or `trash/restore`. */
  endpoint: string
  /** The decoded JSON body, or `null` for a request without one. */
  body: Record<string, unknown> | null
}

/**
 * A trashed entry, the fields a spec states; the rest is filled with plain values.
 * @param id The trash id.
 * @param name The entry's name.
 * @param overrides Anything else the spec cares about.
 * @returns The entry as the panel would send it.
 */
export const trashEntry = (id: string, name: string, overrides: Partial<TrashEntry> = {}): TrashEntry => {
  return {
    id,
    name,
    originalPath: `docs/${name}`,
    kind: 'file',
    sizeBytes: 2048,
    deletedAt: '2026-11-01T10:00:00Z',
    expiresAt: '2026-12-01T10:00:00Z',
    ...overrides,
  }
}

/** How the stub searches and sorts the trash the way the panel's sort map names the columns. */
const TRASH_RULES: PagedRules<TrashEntry> = {
  matches: (row, term) => {
    return row.name.toLowerCase().includes(term)
  },
  sorts: {
    name: (row) => {
      return row.name
    },
    deletedAt: (row) => {
      return row.deletedAt
    },
    expiresAt: (row) => {
      return row.expiresAt
    },
    size: (row) => {
      return String(row.sizeBytes).padStart(12, '0')
    },
  },
  filters: {},
}

/**
 * Serves the trash listing from a mutable array: a restore or an empty call made through
 * {@link stubFileOperations} removes rows from it, so the page reading again sees the change.
 * @param page The Playwright page whose network the route is installed on.
 * @param entries The trashed entries; the stub mutates this array.
 * @returns Resolves once the route is installed.
 */
export const stubTrashEntries = async (page: Page, entries: TrashEntry[]): Promise<void> => {
  await page.route(TRASH_PATTERN, async (route: Route) => {
    if (route.request().method() !== 'GET') {
      await route.fallback()
      return
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(pagedAnswer(entries, route.request().url(), TRASH_RULES)),
    })
  })
}

/**
 * Answers every file-changing endpoint as the panel does when it succeeds, recording each request.
 * Register it AFTER the listing stubs: a read it does not own is passed on to them.
 *
 * A batch answers one outcome per requested item; a subject named in `failing` is refused with the
 * given reason, so a spec can watch a partial failure shown. A restore or an empty also edits the
 * trash array it is given, the way the real panel's state would change.
 * @param page The Playwright page whose network the route is installed on.
 * @param failing The subjects (paths or trash ids) to refuse, by the reason to give.
 * @param trash The trash array a restore or an empty edits, when a spec has one.
 * @returns A function reporting every change request so far, oldest first.
 */
export const stubFileOperations = async (
  page: Page,
  failing: Readonly<Record<string, string>> = {},
  trash: TrashEntry[] = [],
): Promise<() => readonly RecordedOperation[]> => {
  const calls: RecordedOperation[] = []
  await page.route(OPERATION_PATTERN, async (route: Route) => {
    const request = route.request()
    if (request.method() === 'GET') {
      await route.fallback()
      return
    }
    const endpoint = new URL(request.url()).pathname.replace('/api/v1/files/', '')
    const body = request.postDataJSON() as Record<string, unknown> | null
    calls.push({ method: request.method(), endpoint, body })
    const pathOfBody = typeof body?.path === 'string' ? body.path : ''
    const respond = (json: unknown): Promise<void> => {
      return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(json) })
    }
    const batch = (subjects: string[]): FileBatchResult => {
      return {
        items: subjects.map((subject) => {
          const reason = failing[subject]
          return reason === undefined
            ? { subject, succeeded: true, code: null, message: null }
            : { subject, succeeded: false, code: 'Refused', message: reason }
        }),
      }
    }
    if (endpoint === 'entries' || endpoint === 'rename') {
      await respond({ name: 'x', path: pathOfBody, kind: 'file', sizeBytes: 0, modifiedAt: '2026-11-01T10:00:00Z', mode: '644', isSymlink: false })
    } else if (endpoint === 'permissions') {
      await respond(batch([pathOfBody]))
    } else if (endpoint === 'trash/restore') {
      const ids = (body?.trashIds ?? []) as string[]
      const result = batch(ids)
      for (const item of result.items.filter((outcome) => {return outcome.succeeded})) {
        trash.splice(trash.findIndex((entry) => {return entry.id === item.subject}), 1)
      }
      await respond(result)
    } else if (endpoint === 'trash' && request.method() === 'DELETE') {
      const count = trash.length
      trash.splice(0, trash.length)
      await respond({ count })
    } else if (endpoint === 'archive') {
      await respond({ sizeBytes: 1024 })
    } else if (endpoint === 'extract') {
      await respond({ entryCount: 3 })
    } else {
      const paths = (body?.sourcePaths ?? body?.paths ?? []) as string[]
      await respond(batch(paths))
    }
  })
  return () => {
    return calls
  }
}
