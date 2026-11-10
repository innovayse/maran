import { expect, test } from '../fixtures/guarded-test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubEmptyModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import type { PanelCertificate, PanelCertificateReading } from '../../src/types/panelCertificate'
import type { ConsoleMessage, Page } from '@playwright/test'

// The panel's own certificate screen (issue #132): the self-signed fallback, an order, and a panel
// with no host name to order for (issue #417: read as a 200 flagged `hostNameConfigured: false`, so the
// visit leaves no failed request and no console error).

const PANEL_CERTIFICATE_PATTERN = '**/api/v1/panel-certificate'

/** The panel-certificate URL as the guard matches a refusal on it. */
const PANEL_CERTIFICATE_URL = /\/api\/v1\/panel-certificate$/

/** The settings the security-policy screen reads on the way to the certificate screen. */
const POLICY_PATTERN = '**/api/v1/security-policy'

const ISSUED: PanelCertificate = {
  hostName: 'panel.example.net',
  expiresAt: '2027-02-01T00:00:00Z',
  issuedAt: '2026-11-03T00:00:00Z',
}

// `stubShellReads` answers the reads an administrator's shell makes on every screen (servers, pinned
// pages, needs-attention and updates figures). Unanswered they reach no backend and come back as 502s,
// which the quiet-browser case below would count as the screen's own failures.
const shell = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubShellReads(page)
  await stubHealthy(page)
  await stubEmptyModules(page)
}

test('a panel on its self-signed certificate orders a trusted one and shows it', async ({ page, guard }) => {
  // The self-signed state is the panel answering 404 to the status read.
  guard.allow(404, PANEL_CERTIFICATE_URL)
  await shell(page)
  await page.route(POLICY_PATTERN, async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        forceTwoFactorForAdmins: false,
        forceTwoFactorForResellers: false,
        lockoutMinutes: 15,
        maxFailedLoginAttempts: 5,
        minimumPasswordLength: 12,
      }),
    })
  })
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

test('a refused order shows the panel’s own sentence and keeps the self-signed state', async ({ page, guard }) => {
  // Both refusals are the subject: no certificate yet (404) and the order refused (503).
  guard.allow(404, PANEL_CERTIFICATE_URL)
  guard.allow(503, PANEL_CERTIFICATE_URL)
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

test('a panel with no host name shows what to configure, offers no order and leaves the browser quiet', async ({
  page,
}) => {
  await shell(page)
  const consoleErrors: string[] = []
  const failedStatuses: number[] = []
  page.on('console', (message: ConsoleMessage) => {
    if (message.type() === 'error') {
      consoleErrors.push(message.text())
    }
  })
  page.on('response', (response) => {
    if (response.url().includes('/api/v1/') && response.status() >= 400) {
      failedStatuses.push(response.status())
    }
  })
  const unconfigured: PanelCertificateReading = {
    hostNameConfigured: false,
    hostNameNotice: 'The panel has no host name configured.',
    hostName: null,
    expiresAt: null,
    issuedAt: null,
  }
  await page.route(PANEL_CERTIFICATE_PATTERN, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(unconfigured) })
  })

  await page.goto('/settings/panel-certificate')

  await expect(page.getByText('The panel has no host name configured.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Request a trusted certificate' })).toHaveCount(0)
  expect(failedStatuses).toEqual([])
  expect(consoleErrors).toEqual([])
})

test('a panel with a host name shows the recorded certificate from a flagged reading', async ({ page }) => {
  await shell(page)
  const configured: PanelCertificateReading = {
    hostNameConfigured: true,
    hostNameNotice: null,
    hostName: ISSUED.hostName,
    expiresAt: ISSUED.expiresAt,
    issuedAt: ISSUED.issuedAt,
  }
  await page.route(PANEL_CERTIFICATE_PATTERN, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(configured) })
  })

  await page.goto('/settings/panel-certificate')

  await expect(page.getByText('panel.example.net')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Renew now' })).toBeVisible()
})

// Returning to a self-signed certificate (issue #425): offered only while a trusted certificate is
// recorded, confirmed in a dialog that names the consequence, and followed by a fresh read.

const RECORDED: PanelCertificateReading = {
  hostNameConfigured: true,
  hostNameNotice: null,
  hostName: ISSUED.hostName,
  expiresAt: ISSUED.expiresAt,
  issuedAt: ISSUED.issuedAt,
}

test('a panel on a trusted certificate returns to a self-signed one after a confirmation and reads the status again', async ({
  page,
  guard,
}) => {
  // After the return the status read answers 404: the panel is on its self-signed certificate again.
  guard.allow(404, PANEL_CERTIFICATE_URL)
  await shell(page)
  let restored = false
  let reads = 0
  await page.route(PANEL_CERTIFICATE_PATTERN, async (route) => {
    if (route.request().method() === 'DELETE') {
      restored = true
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ hostName: ISSUED.hostName, expiresAt: ISSUED.expiresAt }),
      })
      return
    }
    reads += 1
    if (restored) {
      await route.fulfill({
        status: 404,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'PanelCertificateNotIssued', title: 'Not issued', detail: 'Not issued.' }),
      })
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(RECORDED) })
  })

  await page.goto('/settings/panel-certificate')
  await page.getByRole('button', { name: 'Return to a self-signed certificate' }).click()

  const dialog = page.getByRole('dialog')
  await expect(dialog.getByTestId('confirm-message')).toContainText('browsers will warn')
  expect(restored).toBe(false)
  await dialog.getByRole('button', { name: 'Return to self-signed' }).click()

  await expect(page.getByText('The panel is back on a self-signed certificate.', { exact: false })).toBeVisible()
  await expect(page.getByText('The panel is served with its self-signed certificate', { exact: false })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Return to a self-signed certificate' })).toHaveCount(0)
  expect(restored).toBe(true)
  expect(reads).toBe(2)
})

test('cancelling the confirmation sends nothing and keeps the trusted certificate', async ({ page }) => {
  await shell(page)
  let restoreRequests = 0
  await page.route(PANEL_CERTIFICATE_PATTERN, async (route) => {
    if (route.request().method() === 'DELETE') {
      restoreRequests += 1
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(RECORDED) })
  })

  await page.goto('/settings/panel-certificate')
  await page.getByRole('button', { name: 'Return to a self-signed certificate' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click()

  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(page.getByText('panel.example.net')).toBeVisible()
  expect(restoreRequests).toBe(0)
})

test('a refused return shows the panel’s own sentence and keeps the trusted certificate', async ({ page, guard }) => {
  // The refusal is the subject: the agent does not answer, and the panel says so with a 503.
  guard.allow(503, PANEL_CERTIFICATE_URL)
  await shell(page)
  await page.route(PANEL_CERTIFICATE_PATTERN, async (route) => {
    if (route.request().method() === 'DELETE') {
      await route.fulfill({
        status: 503,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'AgentUnavailable', title: 'Refused', detail: 'The agent is not answering.' }),
      })
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(RECORDED) })
  })

  await page.goto('/settings/panel-certificate')
  await page.getByRole('button', { name: 'Return to a self-signed certificate' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Return to self-signed' }).click()

  await expect(page.getByText('The agent is not answering.')).toBeVisible()
  await expect(page.getByText('panel.example.net')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Return to a self-signed certificate' })).toBeVisible()
})

test('a panel on its self-signed certificate offers no return to it', async ({ page, guard }) => {
  // The self-signed state is the panel answering 404 to the status read.
  guard.allow(404, PANEL_CERTIFICATE_URL)
  await shell(page)
  await page.route(PANEL_CERTIFICATE_PATTERN, async (route) => {
    await route.fulfill({
      status: 404,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'PanelCertificateNotIssued', title: 'Not issued', detail: 'Not issued.' }),
    })
  })

  await page.goto('/settings/panel-certificate')

  await expect(page.getByText('The panel is served with its self-signed certificate', { exact: false })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Return to a self-signed certificate' })).toHaveCount(0)
})
