import { expect, test, type Page } from '@playwright/test'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import {
  logEndFrame,
  logLineFrame,
  stubPhpVersions,
  stubSiteDetail,
  stubOpenSiteLogStream,
  stubSiteLogStream,
  stubSites,
} from '../fixtures/stub-sites-routes'
import type { PanelModule } from '../../src/types/module'
import type { SiteDetail } from '../../src/types/site'

const LICENSED_SITES: PanelModule[] = [
  { name: 'sites', displayName: 'Sites', tier: 'included', isEnabled: true },
]

const SITE: SiteDetail = {
  id: '11111111-1111-1111-1111-111111111111',
  accountId: '22222222-2222-2222-2222-222222222222',
  domain: 'alpha.example.com',
  aliases: [],
  backendType: 'static',
  phpVersion: '',
  proxyUpstream: '',
  documentRoot: '/home/alpha/sites/alpha.example.com/public',
  hasCertificate: false,
  status: 'enabled',
  createdAt: '2026-08-01T10:00:00Z',
}

/**
 * Installs the shell, the site and a log stream serving the given SSE body.
 * @param page The Playwright page whose network the routes are installed on.
 * @param streamBody The raw `text/event-stream` body the log endpoint answers with.
 * @returns Resolves once every route is installed.
 */
const stubLogsTab = async (page: Page, streamBody: string): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubSites(page, [])
  await stubSiteDetail(page, SITE)
  await stubPhpVersions(page, [])
  await stubSiteLogStream(page, streamBody)
}

/**
 * Opens the site's Logs tab and starts a tail.
 * @param page The Playwright page to drive.
 * @returns Resolves once the tail has been requested.
 */
const openLogsAndTail = async (page: Page): Promise<void> => {
  await page.goto(`/sites/${SITE.id}`)
  await page.getByRole('button', { name: 'Logs' }).click()
  await page.getByRole('button', { name: 'Start tailing' }).click()
}

// rules/vue.md: a log line is customer-supplied text, and building markup from it is an XSS hole
// in a panel that renders exactly this kind of content. It goes through interpolation, always.
test('a log line carrying markup is rendered as text, never as an element', async ({ page }) => {
  const hostile = '<img src=x onerror="window.__pwned = true"> GET / 200'
  await stubLogsTab(page, logLineFrame(hostile) + logEndFrame('completed'))

  await openLogsAndTail(page)

  await expect(page.getByText(hostile)).toBeVisible()
  await expect(page.locator('ol img')).toHaveCount(0)
  const scriptRan = await page.evaluate(() => {
    return '__pwned' in window
  })
  expect(scriptRan).toBe(false)
})

// The whole point of naming an ending: a stream that was cut short must never look like one that
// finished. These two assertions are the difference on screen.
test('a truncated ending is reported as an error, not as a normal end', async ({ page }) => {
  await stubLogsTab(page, logLineFrame('GET / 200') + logEndFrame('truncated'))

  await openLogsAndTail(page)

  const notice = page.getByText('The stream ended without saying why — lines may be missing.')
  await expect(notice).toBeVisible()
  // Shown in the error tone, which is what stops it reading as a routine note.
  await expect(notice).toHaveAttribute('aria-live', 'assertive')
  await expect(page.getByText('The log ended: the server had nothing further to send.')).toHaveCount(0)
})

test('a completed ending is reported as a normal end, not as a truncation', async ({ page }) => {
  await stubLogsTab(page, logLineFrame('GET / 200') + logEndFrame('completed'))

  await openLogsAndTail(page)

  const notice = page.getByText('The log ended: the server had nothing further to send.')
  await expect(notice).toBeVisible()
  await expect(notice).toHaveAttribute('aria-live', 'polite')
  await expect(page.getByText('lines may be missing')).toHaveCount(0)
})

// A stream that closes without naming an ending is the dangerous case: the panel reports it as
// `truncated` rather than guessing, and this screen must not soften that back into a normal end.
test('a stream that closes without naming an ending is reported as possibly incomplete', async ({
  page,
}) => {
  await stubLogsTab(page, logLineFrame('GET / 200'))

  await openLogsAndTail(page)

  await expect(page.getByText('lines may be missing')).toBeVisible()
})

test('a dropped ending tells the operator to start tailing again', async ({ page }) => {
  await stubLogsTab(page, logLineFrame('GET / 200') + logEndFrame('dropped'))

  await openLogsAndTail(page)

  await expect(page.getByText('Start tailing again to continue.', { exact: false })).toBeVisible()
})

test('replayed history is labelled so it is not read as live traffic', async ({ page }) => {
  await stubLogsTab(
    page,
    logLineFrame('GET /old 200', true) + logLineFrame('GET /new 200') + logEndFrame('completed'),
  )

  await openLogsAndTail(page)

  const historical = page.getByRole('listitem').filter({ hasText: 'GET /old 200' })
  await expect(historical).toContainText('History')
  const live = page.getByRole('listitem').filter({ hasText: 'GET /new 200' })
  await expect(live).not.toContainText('History')
})

test('the logs tab starts from its empty state rather than an open connection', async ({ page }) => {
  await stubLogsTab(page, logEndFrame('completed'))

  await page.goto(`/sites/${SITE.id}`)
  await page.getByRole('button', { name: 'Logs' }).click()

  await expect(page.getByText('Nothing to show yet')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Start tailing' })).toBeVisible()
})

// The view's own truncation. The store keeps a bounded scrollback, so past that bound the top of
// this pane is no longer the top of the log — and an operator scrolling up must be told so rather
// than reading the oldest line held as the oldest line there was.
test('the pane says so once its own scrollback has dropped the oldest lines', async ({ page }) => {
  // One past the store's cap, which is the smallest input that proves the notice is driven by a
  // line actually being dropped rather than by the buffer merely being full.
  const overflowing = 2_001
  const frames = Array.from({ length: overflowing }, (_line, index) => {
    return logLineFrame(`GET /page-${String(index)} 200`)
  }).join('')
  await stubLogsTab(page, frames + logEndFrame('completed'))

  await openLogsAndTail(page)

  await expect(page.getByText('The oldest lines have scrolled out of this view.')).toBeVisible()
  await expect(page.getByText('2000 lines held')).toBeVisible()
  await expect(page.getByText('GET /page-0 200', { exact: true })).toHaveCount(0)
})

// A stream nobody aborts keeps its connection open for the life of the tab. Leaving the tab must
// end it, and the pane must say the tail stopped rather than leaving the operator to guess.
test('leaving the logs tab ends the open stream instead of leaving it running', async ({ page }) => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED_SITES)
  await stubSites(page, [])
  await stubSiteDetail(page, SITE)
  await stubPhpVersions(page, [])
  await stubOpenSiteLogStream(page)

  await openLogsAndTail(page)
  await expect(page.getByText('Waiting for the first line…')).toBeVisible()

  await page.getByRole('button', { name: 'Overview' }).click()
  await page.getByRole('button', { name: 'Logs' }).click()

  await expect(page.getByText('Tailing stopped.')).toBeVisible()
})
