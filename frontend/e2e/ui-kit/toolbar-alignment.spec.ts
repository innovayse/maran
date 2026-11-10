import { expect, test } from '../fixtures/guarded-test'
import { expectAligned, measureToolbar } from '../fixtures/measure-toolbar'
import { setPersistedLocale } from '../fixtures/set-locale'
import { stubAccountNames, stubAccounts } from '../fixtures/stub-accounts-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { fileEntry, stubFileEntries, stubFilesOptions } from '../fixtures/stub-file-browser-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { Account } from '../../src/types/account'
import type { Page } from '@playwright/test'

// A toolbar lays its controls on one line: every control is the same height (the kit's one control
// scale) and the controls on a line share a centre line. Measured on the kit's own harness and on
// the real file manager toolbar, at a desktop and at a phone width, with an inverse control: a
// planted control off the scale must fail the same measurement.

const DESKTOP = { width: 1440, height: 900 }
const PHONE = { width: 390, height: 844 }

const ALICE: Account = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'alice',
  primaryDomain: 'alice.example.com',
  planId: '44444444-4444-4444-4444-444444444444',
  status: 'active',
  statusName: 'Active',
  createdAt: '2026-08-01T10:00:00Z',
  resellerId: null,
  resellerName: null,
}

/**
 * Opens the toolbar harness.
 * @param page The Playwright page.
 * @param query The harness's query string.
 * @returns Resolves once the toolbar is rendered.
 */
const openHarness = async (page: Page, query: string): Promise<void> => {
  await page.goto(`/e2e/fixtures/toolbar-harness.html?${query}`)
  await expect(page.getByTestId('toolbar')).toBeVisible()
}

for (const viewport of [DESKTOP, PHONE]) {
  test(`every kit control on a toolbar has one height and one centre line at ${viewport.width}px`, async ({ page }) => {
    await page.setViewportSize(viewport)
    await openHarness(page, '')

    const boxes = await measureToolbar(page.getByTestId('toolbar'))

    expect(boxes).toHaveLength(7)
    expectAligned(boxes)
  })

  test(`a control off the height scale is caught by the same measurement at ${viewport.width}px`, async ({ page }) => {
    await page.setViewportSize(viewport)
    await openHarness(page, 'plant=1')

    const boxes = await measureToolbar(page.getByTestId('toolbar'))

    expect(boxes).toHaveLength(8)
    expect(() => {
      expectAligned(boxes)
    }).toThrow()
  })

  test(`a search label inside a toolbar is hidden but still names the field at ${viewport.width}px`, async ({ page }) => {
    await page.setViewportSize(viewport)
    await openHarness(page, '')

    await expect(page.getByRole('searchbox', { name: 'Search this folder' })).toBeVisible()
    const label = page.getByText('Search this folder', { exact: true })
    const box = await label.boundingBox()
    expect(box === null || (box.width <= 1 && box.height <= 1)).toBe(true)
  })

  test(`the file manager toolbar is aligned at ${viewport.width}px`, async ({ page }) => {
    await page.setViewportSize(viewport)
    await setPersistedLocale(page, 'en')
    await stubSignedIn(page)
    await stubHealthy(page)
    await stubModules(page, [
      { name: 'files', displayName: 'Files', kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
    ])
    await stubShellReads(page)
    await stubAccounts(page, [ALICE])
    await stubAccountNames(page, [ALICE])
    await stubFilesOptions(page)
    await stubFileEntries(page, { '': [fileEntry('', 'notes.txt', { sizeBytes: 1536 })] })

    await page.goto(`/files?account=${ALICE.id}`)
    await expect(page.getByRole('switch', { name: 'Show hidden files' })).toBeVisible()

    const boxes = await measureToolbar(page.getByTestId('toolbar'))

    expect(boxes).toHaveLength(5)
    expectAligned(boxes)
  })
}
