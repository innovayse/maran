import type { Page } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type { BackupDestination } from '../../src/types/backupDestination'

/** The destinations endpoint, with or without a query. */
const DESTINATIONS_PATTERN = /\/api\/v1\/backup-destinations(\?.*)?$/

/** How the destinations endpoint searches, sorts and filters, as the panel does. */
const DESTINATION_RULES: PagedRules<BackupDestination> = {
  matches: (destination, term) => {
    return destination.displayName.toLowerCase().includes(term) || destination.name.toLowerCase().includes(term)
  },
  sorts: {
    name: (destination) => {
      return destination.displayName
    },
    createdAt: (destination) => {
      return destination.createdAt
    },
  },
  filters: {
    kind: (destination, value) => {
      return destination.kind === value
    },
  },
}

/**
 * Fulfils `GET /api/v1/backup-destinations` as a page honouring `page`, `pageSize`, `search`, `sort`
 * and `kind`.
 *
 * The rows are any shape that is at least the panel's wire shape: a spec proving the screen drops
 * a member the panel should never send (a credential) needs to be able to send one.
 * @template T The row shape, the wire shape or a wider one.
 * @param page The Playwright page whose network the route is installed on.
 * @param destinations The destinations the stub knows, in the panel's default order.
 * @returns A function reporting every list request so far, oldest first.
 */
export const stubBackupDestinations = async <T extends BackupDestination>(
  page: Page,
  destinations: readonly T[],
): Promise<() => readonly URL[]> => {
  const listRequests: URL[] = []

  await page.route(DESTINATIONS_PATTERN, async (route) => {
    listRequests.push(new URL(route.request().url()))
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(pagedAnswer(destinations, route.request().url(), DESTINATION_RULES)),
    })
  })

  return () => {
    return listRequests
  }
}

/**
 * Fulfils `GET /api/v1/backup-destinations` with an RFC 7807 refusal, so a spec can assert the
 * screen renders the backend's own already-localized sentence.
 * @param page The Playwright page whose network the route is installed on.
 * @param status The HTTP status of the refusal.
 * @param code The machine-stable code the problem carries.
 * @param detail The backend-localized message.
 * @returns Resolves once the route is installed.
 */
export const stubBackupDestinationsRefusal = async (
  page: Page,
  status: number,
  code: string,
  detail: string,
): Promise<void> => {
  await page.route(DESTINATIONS_PATTERN, async (route) => {
    await route.fulfill({
      status,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code, title: 'Forbidden', detail }),
    })
  })
}
