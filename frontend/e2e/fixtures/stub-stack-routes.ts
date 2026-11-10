import type { Page, Route } from '@playwright/test'
import type { StackStatus } from '../../src/types/hostApp'

/** The stack-status endpoint, readable by every signed-in role. */
const STACK_PATTERN = /\/api\/v1\/apps\/stack(\?.*)?$/

/**
 * A host that is ready for sites: nginx installed, one PHP version and one Node.js major. The quiet
 * answer {@link stubSignedIn} installs, so a spec about anything else is never held on the setup
 * wizard and its site form offers the PHP backend as it always did.
 */
export const READY_STACK: StackStatus = {
  isReady: true,
  webServer: 'nginx',
  phpVersions: ['8.3'],
  nodeVersions: ['22'],
}

/** A fresh host: no site web server and no runtime, which the stack guard holds on the wizard. */
export const EMPTY_STACK: StackStatus = {
  isReady: false,
  webServer: null,
  phpVersions: [],
  nodeVersions: [],
}

/**
 * Fulfils `GET /api/v1/apps/stack` with a status, or with what a function returns at the moment of
 * each request, so a spec can let the host become ready while the page is open.
 * @param page The Playwright page whose network the route is installed on.
 * @param status The status to answer, or a function producing it per request.
 * @returns A function reporting how many times the status was read.
 */
export const stubStack = async (page: Page, status: StackStatus | (() => StackStatus)): Promise<() => number> => {
  let reads = 0
  await page.route(STACK_PATTERN, async (route: Route) => {
    reads += 1
    const body = typeof status === 'function' ? status() : status
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
  })
  return (): number => {
    return reads
  }
}
