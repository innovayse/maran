import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import type { FirewallRule } from '../../src/types/firewall'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [
  { name: 'firewall', displayName: 'Firewall', tier: 'included', isEnabled: true },
]

/** One rule-change request the page sent, recorded well enough to assert on exactly. */
interface RecordedChange {
  /** `POST` for an allow, `DELETE` for a removal. */
  method: string
  /** The request body a `POST` carried, decoded; `null` for a `DELETE`, which carries none. */
  body: unknown
  /** The query parameters a `DELETE` carried, decoded; empty for a `POST`, which carries none. */
  query: Record<string, string>
}

/**
 * Puts the firewall screen in front of an administrator with the given rules in force, and starts
 * recording every rule-change request the page sends — the body and the query, not merely that a
 * request happened. A preset is a shortcut to the request the raw form would send, and the whole
 * point of this file is proving the shortcut composes the SAME request rather than merely that a
 * click produced some network activity.
 * @param page The Playwright page under test.
 * @param rules The rules the stubbed panel reports.
 * @returns The recorded requests, newest last.
 */
const openScreen = async (page: Page, rules: FirewallRule[]): Promise<RecordedChange[]> => {
  const changes: RecordedChange[] = []
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubFirewall(page, { rules, bans: [], whitelist: [] })
  page.on('request', (request) => {
    const method = request.method()
    if ((method !== 'POST' && method !== 'DELETE') || !request.url().includes('/api/v1/firewall/rules')) {
      return
    }
    const query = Object.fromEntries(new URL(request.url()).searchParams.entries())
    changes.push({ method, body: method === 'POST' ? request.postDataJSON() : null, query })
  })
  await page.goto('/firewall')
  return changes
}

/**
 * Confirms the pending lockout dialog, the way an operator removing a rule has to.
 * @param page The Playwright page under test.
 * @returns Resolves once the confirm control has been clicked.
 */
const confirmRemoval = async (page: Page): Promise<void> => {
  await page.getByRole('dialog').getByRole('button', { name: 'Remove the rule' }).click()
}

// The web preset opens both ports to every source, which is why `requestAllow` sends it straight
// through with no lockout confirmation: 0.0.0.0/0 replaces nothing an SSH rule could be relying on.
// If the button merely toggled some local flag instead of sending a request, or sent the ports in
// the wrong order, or with a narrowed source, this is the test that would catch it — a passing
// click assertion alone would not.
test('the web preset opens exactly 80 and 443, open to every source, in that order', async ({ page }) => {
  const changes = await openScreen(page, [])

  await page.getByRole('button', { name: 'Open the web ports (80 and 443)' }).click()

  await expect
    .poll(() => {
      return changes
    })
    .toHaveLength(2)
  expect(changes[0]).toEqual({
    method: 'POST',
    body: { port: 80, protocol: 'tcp', sourceCidr: '0.0.0.0/0' },
    query: {},
  })
  expect(changes[1]).toEqual({
    method: 'POST',
    body: { port: 443, protocol: 'tcp', sourceCidr: '0.0.0.0/0' },
    query: {},
  })
  await expect(page.getByRole('row').filter({ hasText: '443' })).toBeVisible()
})

// A preset that re-sent a port already open would fail on a duplicate-rule conflict for no reason
// an operator could act on (`FirewallPresetButtons`'s own reasoning) — so only the missing port may
// be requested.
test('the web preset does not re-request a port the host already has open', async ({ page }) => {
  const changes = await openScreen(page, [{ port: 80, protocol: 'tcp', sourceCidr: '0.0.0.0/0' }])

  await page.getByRole('button', { name: 'Open the web ports (80 and 443)' }).click()

  await expect
    .poll(() => {
      return changes
    })
    .toHaveLength(1)
  expect(changes[0]).toEqual({
    method: 'POST',
    body: { port: 443, protocol: 'tcp', sourceCidr: '0.0.0.0/0' },
    query: {},
  })
})

// Both ports already open leaves the preset nothing to do, and the button says so by disabling
// itself rather than by silently sending an empty batch a reader could mistake for "nothing changed
// because it worked".
test('the web preset is disabled once both ports are already open', async ({ page }) => {
  const changes = await openScreen(page, [
    { port: 80, protocol: 'tcp', sourceCidr: '0.0.0.0/0' },
    { port: 443, protocol: 'tcp', sourceCidr: '0.0.0.0/0' },
  ])

  await expect(page.getByRole('button', { name: 'Open the web ports (80 and 443)' })).toBeDisabled()
  expect(changes).toEqual([])
})

// Turning the MySQL toggle on sends the exact same shape of request the raw form would for that
// port: open to every source, which is why it too skips the lockout confirmation.
test('turning the MySQL toggle on sends exactly the rule it advertises', async ({ page }) => {
  const changes = await openScreen(page, [])

  await page.getByRole('switch', { name: 'MySQL reachable from outside (3306)' }).click()

  await expect
    .poll(() => {
      return changes
    })
    .toHaveLength(1)
  expect(changes[0]).toEqual({
    method: 'POST',
    body: { port: 3306, protocol: 'tcp', sourceCidr: '0.0.0.0/0' },
    query: {},
  })
  await expect(page.getByRole('switch', { name: 'MySQL reachable from outside (3306)' })).toHaveAttribute(
    'aria-checked',
    'true',
  )
})

// Turning it off is a removal, so it goes through the same lockout confirmation every removal
// does (`FirewallPage.requestDeny`) — the toggle buys no exemption from the question a "Remove"
// row action would also be asked.
test('turning the MySQL toggle off asks first, then sends exactly the rule the panel is running', async ({
  page,
}) => {
  const changes = await openScreen(page, [{ port: 3306, protocol: 'tcp', sourceCidr: '0.0.0.0/0' }])

  await page.getByRole('switch', { name: 'MySQL reachable from outside (3306)' }).click()

  const dialog = page.getByRole('dialog')
  await expect(dialog).toContainText('Remove this rule?')
  await expect(dialog).toContainText('tcp/3306 from 0.0.0.0/0')
  expect(changes).toEqual([])

  await confirmRemoval(page)

  await expect
    .poll(() => {
      return changes
    })
    .toHaveLength(1)
  expect(changes[0]).toEqual({
    method: 'DELETE',
    body: null,
    query: { port: '3306', protocol: 'tcp', sourceCidr: '0.0.0.0/0' },
  })
})

// `FirewallPresetButtons` reads MySQL's rules by port alone, on purpose: a rule scoped to a
// narrower range than everything still counts as "open", and turning the toggle off has to remove
// what is ACTUALLY there — every scoped rule, each named back exactly as the listing reported it —
// or the toggle would read closed over a port the firewall was still answering on.
test('turning the MySQL toggle off removes every rule open on that port, each named exactly', async ({
  page,
}) => {
  const changes = await openScreen(page, [
    { port: 3306, protocol: 'tcp', sourceCidr: '0.0.0.0/0' },
    { port: 3306, protocol: 'tcp', sourceCidr: '203.0.113.0/24' },
  ])

  await page.getByRole('switch', { name: 'MySQL reachable from outside (3306)' }).click()
  await confirmRemoval(page)

  await expect
    .poll(() => {
      return changes
    })
    .toHaveLength(2)
  expect(changes).toEqual([
    { method: 'DELETE', body: null, query: { port: '3306', protocol: 'tcp', sourceCidr: '0.0.0.0/0' } },
    {
      method: 'DELETE',
      body: null,
      query: { port: '3306', protocol: 'tcp', sourceCidr: '203.0.113.0/24' },
    },
  ])
  await expect(page.getByRole('switch', { name: 'MySQL reachable from outside (3306)' })).toHaveAttribute(
    'aria-checked',
    'false',
  )
})
