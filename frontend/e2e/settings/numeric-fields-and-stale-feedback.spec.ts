import { expect, test } from '../fixtures/guarded-test'
import { mainNavigation, openFullShell, openSection } from '../fixtures/open-full-shell'
import { AUDIT_VERIFICATION_PATTERN, stubAudit, stubAuditChain } from '../fixtures/stub-audit-routes'
import { storedSmtpSettings, stubSmtpSettings } from '../fixtures/stub-smtp-routes'
import type { SecurityPolicy } from '../../src/types/securityPolicy'
import type { Page } from '@playwright/test'

// Issues #504 and #508. A number field holding text is faulted beside the field and nothing is sent
// (the server's own refusal would have named an internal parameter), and the outcome of an earlier
// visit's save or verification is not shown on the next visit.

const POLICY_PATTERN = '**/api/v1/security-policy'

const POLICY: SecurityPolicy = {
  minimumPasswordLength: 12,
  forceTwoFactorForAdmins: true,
  forceTwoFactorForResellers: true,
  maxFailedLoginAttempts: 5,
  lockoutMinutes: 15,
}

/** The sentence the server gives a verification it refused; English because the spec runs in English. */
const REFUSAL = 'The panel is busy sealing the journal. Try again shortly.'

/**
 * Opens the full shell with a security policy the panel reports, counting the saves it receives.
 * @param page The Playwright page under test.
 * @returns A function reporting how many saves reached the panel.
 */
const openPolicy = async (page: Page): Promise<() => number> => {
  await openFullShell(page, '/')
  let saves = 0
  await page.route(POLICY_PATTERN, async (route) => {
    if (route.request().method() === 'PUT') {
      saves += 1
      await route.fulfill({ status: 204 })
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(POLICY) })
  })
  await page.goto('/settings/security-policy')
  await expect(page.getByLabel('How long an account stays locked, in minutes')).toHaveValue('15')
  return () => {
    return saves
  }
}

test('text in a security policy number field is faulted beside the field and never sent', async ({ page }) => {
  const saves = await openPolicy(page)
  const field = page.getByLabel('How long an account stays locked, in minutes')

  await field.fill('abc')
  await page.getByRole('button', { name: 'Save policy' }).click()

  await expect(page.getByText('Enter a whole number, digits only.')).toBeVisible()
  await expect(field).toHaveAttribute('aria-invalid', 'true')
  expect(saves()).toBe(0)
})

test('a whole number in the same field is sent, so the check does not refuse everything', async ({ page }) => {
  const saves = await openPolicy(page)

  await page.getByLabel('How long an account stays locked, in minutes').fill('30')
  await page.getByRole('button', { name: 'Save policy' }).click()

  await expect(page.getByText('The policy has been saved.')).toBeVisible()
  await expect(page.getByText('Enter a whole number, digits only.')).toHaveCount(0)
  expect(saves()).toBe(1)
})

test('text in the SMTP port is faulted beside the field and never sent', async ({ page }) => {
  await openFullShell(page, '/')
  await stubSmtpSettings(page, storedSmtpSettings)
  let saves = 0
  await page.route('**/api/v1/notifications/smtp', async (route) => {
    if (route.request().method() === 'PUT') {
      saves += 1
      await route.fulfill({ status: 204 })
      return
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(storedSmtpSettings) })
  })
  await page.goto('/settings/smtp')
  const port = page.getByLabel('Port', { exact: true })
  await expect(port).toHaveValue(String(storedSmtpSettings.port))

  await port.fill('smtp')
  await page.getByRole('button', { name: 'Save mail settings' }).click()

  await expect(page.getByText('Enter the port as a whole number, digits only.')).toBeVisible()
  expect(saves).toBe(0)

  // The accepting control: a whole number is sent.
  await port.fill('2525')
  await page.getByRole('button', { name: 'Save mail settings' }).click()
  await expect(page.getByText('Enter the port as a whole number, digits only.')).toHaveCount(0)
  expect(saves).toBe(1)
})

test('a refused verification does not survive leaving the audit journal and coming back', async ({ page, guard }) => {
  guard.allow(429, AUDIT_VERIFICATION_PATTERN)
  await openFullShell(page, '/')
  await stubAudit(page, [])
  await stubAuditChain(page)
  await page.route(AUDIT_VERIFICATION_PATTERN, async (route) => {
    await route.fulfill({
      status: 429,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'RateLimited', title: REFUSAL, detail: REFUSAL }),
    })
  })
  const nav = mainNavigation(page)

  await openSection(page, 'Monitoring')
  await nav.getByRole('link', { name: 'Audit journal', exact: true }).click()
  await page.getByRole('button', { name: 'Verify the chain' }).click()
  await expect(page.getByText(REFUSAL)).toBeVisible()

  await nav.getByRole('link', { name: 'Background tasks', exact: true }).click()
  await nav.getByRole('link', { name: 'Audit journal', exact: true }).click()

  await expect(page.getByRole('button', { name: 'Verify the chain' })).toBeVisible()
  await expect(page.getByText(REFUSAL)).toHaveCount(0)
})
