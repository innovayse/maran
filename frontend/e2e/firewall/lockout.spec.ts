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

// A rule scoped to one office range on a high port. It is exactly the shape an SSH restriction
// takes on a host running sshd somewhere other than 22 — and the screen cannot tell that it is one,
// which is the whole reason the confirmation exists.
const SSH_RESTRICTION: FirewallRule = { port: 2222, protocol: 'tcp', sourceCidr: '203.0.113.0/24' }

/** An ordinary open web port, which shares no port number with the rule above. */
const WEB: FirewallRule = { port: 80, protocol: 'tcp', sourceCidr: '0.0.0.0/0' }

/** How the screen names a rule — one line, the way the panel's own audit journal names it. */
const describeRule = (rule: FirewallRule): string => {
  return `${rule.protocol}/${rule.port} from ${rule.sourceCidr}`
}

/**
 * Puts the firewall screen in front of an administrator with the given rules in force, and starts
 * recording every rule change the page sends.
 * @param page The Playwright page under test.
 * @param rules The rules the stubbed panel reports.
 * @returns The list the recorded rule-change requests accumulate into, newest last.
 */
const openScreen = async (page: Page, rules: FirewallRule[]): Promise<string[]> => {
  const changes: string[] = []
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubFirewall(page, { rules, bans: [], whitelist: [] })
  page.on('request', (request) => {
    const method = request.method()
    if ((method === 'POST' || method === 'DELETE') && request.url().includes('/api/v1/firewall/rules')) {
      changes.push(`${method} ${request.url()}`)
    }
  })
  await page.goto('/firewall')
  return changes
}

/**
 * Opens a rule row's actions menu and asks for the removal.
 * @param page The Playwright page under test.
 * @param rule The rule whose row to act on.
 * @returns Resolves once the removal has been asked for.
 */
const askToRemove = async (page: Page, rule: FirewallRule): Promise<void> => {
  const row = page.getByRole('row').filter({ hasText: String(rule.port) })
  await row.getByRole('button', { name: `Actions for ${describeRule(rule)}` }).click()
  await page.getByRole('menuitem', { name: 'Remove' }).click()
}

// Every removal is confirmed, because any rule here can be the one restricting SSH and the screen
// has no way to tell which: the port sshd listens on is a host fact the panel holds and never sends
// to the browser. This is the proposition the plan calls "the SSH-removal confirm appears".
test('removing a rule raises the lockout confirmation before anything is sent', async ({ page }) => {
  const changes = await openScreen(page, [SSH_RESTRICTION])

  await askToRemove(page, SSH_RESTRICTION)

  const dialog = page.getByRole('dialog')
  await expect(dialog).toContainText('Remove this rule?')
  await expect(dialog).toContainText(describeRule(SSH_RESTRICTION))
  await expect(dialog).toContainText('If this is the rule restricting SSH')
  await expect(dialog).toContainText('This screen is not told which port your SSH daemon listens on')
  expect(changes).toEqual([])
})

// The confirmation says which risk THIS change carries: removing the only rule for a port hands the
// port back to the agent's unconditional accept, which is the fail-open half of the design and the
// half an operator has to be told about before they choose it.
test('the confirmation says when a removal leaves no rule at all for that port', async ({ page }) => {
  await openScreen(page, [SSH_RESTRICTION, WEB])

  await askToRemove(page, SSH_RESTRICTION)

  await expect(page.getByRole('dialog')).toContainText('This leaves no rule at all for that port.')
})

// Dismissing the dialog has to leave the firewall untouched. A confirmation that sends the change
// anyway is worse than none: it teaches an operator that the question was rhetorical.
test('dismissing the lockout confirmation sends no rule change at all', async ({ page }) => {
  const changes = await openScreen(page, [SSH_RESTRICTION])

  await askToRemove(page, SSH_RESTRICTION)
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click()

  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(page.getByRole('row').filter({ hasText: '2222' })).toBeVisible()
  expect(changes).toEqual([])
})

// Confirming sends the removal — and sends the source range back exactly as the listing reported
// it. A deny naming a range the allow was not installed with matches nothing and still answers 200,
// which would leave an operator reading a closed port over an open one.
test('confirming the removal sends the rule back spelled exactly as it was reported', async ({
  page,
}) => {
  const changes = await openScreen(page, [SSH_RESTRICTION])

  await askToRemove(page, SSH_RESTRICTION)
  await page.getByRole('dialog').getByRole('button', { name: 'Remove the rule' }).click()

  await expect
    .poll(() => {
      return changes
    })
    .toHaveLength(1)
  expect(changes[0]).toBe(
    `DELETE ${new URL('/api/v1/firewall/rules?port=2222&protocol=tcp&sourceCidr=203.0.113.0%2F24', page.url()).toString()}`,
  )
  await expect(page.getByText('No rules of your own')).toBeVisible()
})

// The other half of the SSH restriction UI: ADDING a rule scoped to a narrower range than
// everything is the only kind of addition that can displace the agent's SSH fallback, so it is
// confirmed too — and, like the removal, nothing is sent until it is.
test('adding a rule scoped to a narrower range raises the confirmation before it is sent', async ({
  page,
}) => {
  const changes = await openScreen(page, [])

  await page.getByRole('textbox', { name: 'Port' }).fill('2222')
  await page.getByRole('textbox', { name: 'Source range' }).fill('203.0.113.0/24')
  await page.getByRole('button', { name: 'Open the port' }).click()

  const dialog = page.getByRole('dialog')
  await expect(dialog).toContainText('Install this rule?')
  await expect(dialog).toContainText('tcp/2222 from 203.0.113.0/24')
  expect(changes).toEqual([])

  await dialog.getByRole('button', { name: 'Install the rule' }).click()

  await expect
    .poll(() => {
      return changes
    })
    .toHaveLength(1)
  expect(changes[0]).toContain('POST ')
  await expect(page.getByRole('row').filter({ hasText: '203.0.113.0/24' })).toBeVisible()
})

// And the discriminator, without which every test above would pass on a screen that confirmed
// everything: an allow open to every source replaces the agent's fallback with an identical accept
// and cannot cut anybody off, so it is not worth a question.
test('adding a rule open to every source is sent without a confirmation', async ({ page }) => {
  const changes = await openScreen(page, [])

  await page.getByRole('textbox', { name: 'Port' }).fill('8080')
  await page.getByRole('button', { name: 'Open the port' }).click()

  await expect(page.getByRole('row').filter({ hasText: '8080' })).toBeVisible()
  await expect(page.getByRole('dialog')).toHaveCount(0)
  expect(changes.filter((change) => {
    return change.startsWith('POST ')
  })).toHaveLength(1)
})
