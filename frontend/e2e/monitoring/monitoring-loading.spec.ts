import type { Page } from '@playwright/test'
import { describeRegionLoading } from '../fixtures/expect-region-loading'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubbedBucket, stubMonitoring } from '../fixtures/stub-monitoring-routes'
import type { PanelModule } from '../../src/types/module'

// The monitoring screen while it is on the way (#243): every card keeps its heading and every
// chart its label, over placeholders.

const LICENSED: PanelModule[] = [{ name: 'monitoring', displayName: 'Monitoring', tier: 'included', isEnabled: true }]

/** The server's refusal the failure case answers with. */
const REFUSAL = 'The monitoring data could not be read.'

/** The services read, which the screen waits on together with the chart. */
const SERVICES_PATTERN = /\/api\/v1\/monitoring\/services$/

/**
 * Signs in and stubs every monitoring read. Nothing is stopped and nothing is over quota, so no
 * attention notice lands above the cards when the answer arrives.
 * @param page The Playwright page.
 * @returns Resolves once the routes are installed.
 */
const stubScreen = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubMonitoring(
    page,
    { lastDay: [stubbedBucket(0, 11.5)], lastWeek: [stubbedBucket(0, 91.5)] },
    [{ service: 'webServer', name: 'Web server', state: 'running', detail: 'active (running)' }],
    [],
  )
}

describeRegionLoading('monitoring', {
  stub: stubScreen,
  stubFailure: async (page) => {
    await stubScreen(page)
    await page.route(SERVICES_PATTERN, async (route) => {
      await route.fulfill({
        status: 500,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'HostUnexpectedError', title: 'Unexpected error', detail: REFUSAL }),
      })
    })
  },
  pattern: SERVICES_PATTERN,
  path: '/monitoring',
  anchor: (page) => {
    return page.getByRole('heading', { name: 'Services' })
  },
  contentText: 'Web server',
  failureText: REFUSAL,
})
