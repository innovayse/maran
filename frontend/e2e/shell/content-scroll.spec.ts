import { expect, test } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import type { FirewallRule } from '../../src/types/firewall'

// Issue #221 (point 7): the Firewall page showed two vertical scrollbars and the app bar moved.
// The content panel is the shell's one scroll container, with its scrollbar lane reserved.

// Headless Chromium hides scrollbars unless told otherwise, and a hidden scrollbar takes no width:
// a lane that moved, or a second scrollbar, would then be invisible to these checks. Top-level,
// because a launch option needs a browser of its own.
test.use({ launchOptions: { ignoreDefaultArgs: ['--hide-scrollbars'] } })

test('the Firewall page scrolls in its panel only, and the app bar does not move', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 600 })
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [{ name: 'firewall', displayName: 'Firewall', tier: 'included', isEnabled: true }])
  const rules = Array.from({ length: 40 }, (_, index): FirewallRule => {
    return { port: 2000 + index, portTo: null, protocol: 'tcp', sourceCidr: '0.0.0.0/0' }
  })
  await stubFirewall(page, { rules, bans: [], whitelist: [] })

  await page.goto('/')
  const before = await page.getByRole('banner').boundingBox()

  await page.getByRole('navigation', { name: 'Main navigation' }).getByRole('link', { name: 'Firewall' }).click()
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()

  const documentScrolls = await page.evaluate((): boolean => {
    const root = document.scrollingElement ?? document.documentElement
    return root.scrollHeight > root.clientHeight || root.scrollWidth > root.clientWidth
  })
  expect(documentScrolls).toBe(false)

  const after = await page.getByRole('banner').boundingBox()
  expect(after?.x).toBe(before?.x)
  expect(after?.width).toBe(before?.width)

  // The content panel is the one scroller, and its scrollbar lane is reserved on every page.
  const main = page.getByRole('main')
  const lane = await main.evaluate((element: HTMLElement): { scrolls: boolean; width: number; bar: number } => {
    const borders = element.offsetWidth - element.clientWidth
    return { scrolls: element.scrollHeight > element.clientHeight, width: element.clientWidth, bar: borders - 2 }
  })
  expect(lane.scrolls).toBe(true)
  // Positive control: scrollbars are drawn in this browser, so a lane that moved would show.
  expect(lane.bar).toBeGreaterThan(0)
  await page.getByRole('navigation', { name: 'Main navigation' }).getByRole('link', { name: 'System status' }).click()
  await expect(page).toHaveURL('/')
  const short = await main.evaluate((element: HTMLElement): { scrolls: boolean; width: number } => {
    return { scrolls: element.scrollHeight > element.clientHeight, width: element.clientWidth }
  })
  expect(short.scrolls).toBe(false)
  expect(short.width).toBe(lane.width)
})
