import { expect, test, type Locator, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import type { Ban, FirewallRule, WhitelistEntry } from '../../src/types/firewall'
import type { PanelModule } from '../../src/types/module'

// Issue #218, point 5: the three firewall screens (Rules, Bans, Never banned) are laid out one way,
// their placeholders describe the field rather than look like a value, a refusal goes away once the
// input it was about changes, and the instants on the two lists carry the time of day.

test.use({ timezoneId: 'UTC' })

const LICENSED: PanelModule[] = [{ name: 'firewall', displayName: 'Firewall', tier: 'included', isEnabled: true }]

const RULE: FirewallRule = { port: 8443, portTo: null, protocol: 'tcp', sourceCidr: '0.0.0.0/0' }

const BAN: Ban = {
  id: '00000000-0000-0000-0000-0000000000b2',
  ipAddress: '203.0.113.9',
  reason: 'manual',
  failures: 0,
  bannedAt: '2026-09-01T09:05:00+00:00',
  expiresAt: null,
}

const EXEMPTION: WhitelistEntry = {
  id: '00000000-0000-0000-0000-0000000000c1',
  cidr: '198.51.100.0/24',
  note: 'The office',
  createdAt: '2026-09-02T17:40:00+00:00',
}

/** Each firewall screen, with the submit button of its form and the heading of its list. */
const SCREENS = [
  { path: '/firewall', submit: 'Open the port', list: 'Rules in force' },
  { path: '/firewall/bans', submit: 'Ban the address', list: 'Bans in force' },
  { path: '/firewall/whitelist', submit: 'Add the exemption', list: 'Exempt ranges' },
] as const

/**
 * Puts the firewall in front of an administrator, one row in each list.
 * @param page The Playwright page.
 * @returns Resolves once the routes are installed.
 */
const stubScreens = async (page: Page): Promise<void> => {
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubFirewall(page, { rules: [{ ...RULE }], bans: [{ ...BAN }], whitelist: [{ ...EXEMPTION }] })
}

/**
 * Where an element is drawn, failing the test when it is not drawn at all.
 * @param locator The element.
 * @returns Its bounding box.
 */
const boxOf = async (locator: Locator): Promise<{ y: number; height: number }> => {
  const box = await locator.boundingBox()
  if (box === null) {
    throw new Error('the element is not drawn')
  }
  return box
}

for (const width of [1440, 390]) {
  test(`at ${width}px the three firewall screens put the form first and the list under its own heading`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 900 })
    await stubScreens(page)

    for (const screen of SCREENS) {
      await page.goto(screen.path)
      const submit = page.getByRole('button', { name: screen.submit })
      const heading = page.getByRole('heading', { name: screen.list })
      const table = page.getByRole('table')
      await expect(heading).toBeVisible()
      await expect(table).toBeVisible()

      const submitBox = await boxOf(submit)
      const headingBox = await boxOf(heading)
      const tableBox = await boxOf(table)
      // Form, then the list's heading, then the list: the same order on all three screens.
      expect(submitBox.y + submitBox.height).toBeLessThan(headingBox.y)
      expect(headingBox.y + headingBox.height).toBeLessThanOrEqual(tableBox.y)
      // The form and the list are two cards, never one card holding both.
      const sharedCard = await submit.evaluate((button, headingText) => {
        const card = button.closest('form')?.parentElement
        return card?.textContent?.includes(headingText) ?? false
      }, screen.list)
      expect(sharedCard).toBe(false)
    }
  })
}

test('no firewall placeholder reads like a value somebody already typed', async ({ page }) => {
  await stubScreens(page)
  const looksLikeData = /^(e\.g\.\s*)?[\d./:]+$/

  for (const screen of SCREENS) {
    await page.goto(screen.path)
    await expect(page.getByRole('button', { name: screen.submit })).toBeVisible()
    const placeholders = await page
      .locator('form input[placeholder]')
      .evaluateAll((inputs) => {
        return inputs.map((input) => {return (input as HTMLInputElement).placeholder})
      })
    expect(placeholders.length).toBeGreaterThan(0)
    for (const placeholder of placeholders) {
      expect(placeholder).not.toMatch(looksLikeData)
      expect(placeholder).not.toMatch(/^e\.g\./)
    }
  }
})

test('a refused rule stops being shown once the port it was about is changed', async ({ page }) => {
  await stubScreens(page)
  await page.route('**/api/v1/firewall/rules*', async (route) => {
    if (route.request().method() !== 'POST') {
      await route.fallback()
      return
    }
    await route.fulfill({
      status: 409,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'RuleExists', title: 'Conflict', detail: 'This rule is already installed.' }),
    })
  })
  await page.goto('/firewall')

  await page.getByRole('textbox', { name: 'Port', exact: true }).fill('8443')
  await page.getByRole('button', { name: 'Open the port' }).click()
  const refusal = page.getByText('This rule is already installed.')
  await expect(refusal).toBeVisible()

  await page.getByRole('textbox', { name: 'Port', exact: true }).fill('8444')
  await expect(refusal).toHaveCount(0)
})

test('bans and exemptions say when, to the minute, not only on which day', async ({ page }) => {
  await stubScreens(page)

  await page.goto('/firewall/bans')
  await expect(page.getByRole('row').filter({ hasText: BAN.ipAddress })).toContainText('1 Sep 2026 09:05')

  await page.goto('/firewall/whitelist')
  await expect(page.getByRole('row').filter({ hasText: EXEMPTION.cidr })).toContainText('2 Sep 2026 17:40')
})
