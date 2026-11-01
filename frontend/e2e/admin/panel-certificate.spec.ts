import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import type { PanelCertificate } from '../../src/types/panelCertificate'

// The panel's own certificate screen (issue #132): the self-signed fallback, an order, and a panel
// with no host name to order for.

const PANEL_CERTIFICATE_PATTERN = '**/api/v1/panel-certificate'

const ISSUED: PanelCertificate = {
  hostName: 'panel.example.net',
  expiresAt: '2027-02-01T00:00:00Z',
  issuedAt: '2026-11-03T00:00:00Z',
}

const shell = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
}

test('a panel on its self-signed certificate orders a trusted one and shows it', async ({ page }) => {
  await shell(page)
  let ordered = false
  await page.route(PANEL_CERTIFICATE_PATTERN, async (route) => {
    if (route.request().method() === 'POST') {
      ordered = true
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ISSUED) })
      return
    }
    await route.fulfill({
      status: 404,
      contentType: 'application/problem+json',
      body: JSON.stringify({
        code: 'PanelCertificateNotIssued',
        title: 'Not issued',
        detail: 'No trusted certificate has been issued for the panel.',
      }),
    })
  })

  await page.goto('/settings/security-policy')
  await page.getByRole('link', { name: 'Panel certificate' }).click()

  await expect(page.getByText('The panel is served with its self-signed certificate', { exact: false })).toBeVisible()
  await page.getByRole('button', { name: 'Request a trusted certificate' }).click()

  await expect(page.getByText('The trusted certificate is installed.', { exact: false })).toBeVisible()
  await expect(page.getByText('panel.example.net')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Renew now' })).toBeVisible()
  expect(ordered).toBe(true)
})

test('a refused order shows the panel’s own sentence and keeps the self-signed state', async ({ page }) => {
  await shell(page)
  await page.route(PANEL_CERTIFICATE_PATTERN, async (route) => {
    const refusal =
      route.request().method() === 'POST'
        ? { status: 503, code: 'AgentBelowFreeSpaceFloor', detail: 'The server is nearly out of disk space.' }
        : { status: 404, code: 'PanelCertificateNotIssued', detail: 'Not issued.' }
    await route.fulfill({
      status: refusal.status,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: refusal.code, title: 'Refused', detail: refusal.detail }),
    })
  })

  await page.goto('/settings/panel-certificate')
  await page.getByRole('button', { name: 'Request a trusted certificate' }).click()

  await expect(page.getByText('The server is nearly out of disk space.')).toBeVisible()
  await expect(page.getByText('The panel is served with its self-signed certificate', { exact: false })).toBeVisible()
})

test('a panel with no host name shows what to configure and offers no order', async ({ page }) => {
  await shell(page)
  await page.route(PANEL_CERTIFICATE_PATTERN, async (route) => {
    await route.fulfill({
      status: 409,
      contentType: 'application/problem+json',
      body: JSON.stringify({
        code: 'PanelHostNameNotConfigured',
        title: 'No host name',
        detail: 'The panel has no host name configured.',
      }),
    })
  })

  await page.goto('/settings/panel-certificate')

  await expect(page.getByText('The panel has no host name configured.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Request a trusted certificate' })).toHaveCount(0)
})
