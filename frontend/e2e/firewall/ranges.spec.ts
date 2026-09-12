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

// The passive data range an FTPS host needs, which is the reason a rule learned to name more than
// one port at all. It is here as an ordinary rule an administrator created, because that is what it
// is: the FTPS screen shows which ports the firewall would have to open and offers to ask for them,
// and this module is the one that opens them.
const PASSIVE_RANGE: FirewallRule = {
  port: 30000,
  portTo: 30099,
  protocol: 'tcp',
  sourceCidr: '0.0.0.0/0',
}

/** The FTPS control port beside it — a single port, so its bound is null. */
const CONTROL_PORT: FirewallRule = { port: 21, portTo: null, protocol: 'tcp', sourceCidr: '0.0.0.0/0' }

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
 * Puts the firewall screen in front of an administrator with the given rules in force, and records
 * every rule-change request the page sends — the body and the query, not merely that a request
 * happened.
 *
 * The value and not a bound is the whole point of this file: "the rule was created" passes against
 * a range that silently collapsed to its first port, which is ninety-nine ports an administrator
 * does not know are open.
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
 * Fills the rule form and submits it.
 * @param page The Playwright page under test.
 * @param port The port to type into the first field.
 * @param portTo The upper bound to type into the second, or the empty string to leave it alone.
 * @returns Resolves once the submit control has been pressed.
 */
const openPorts = async (page: Page, port: string, portTo: string): Promise<void> => {
  await page.getByRole('textbox', { name: 'Port', exact: true }).fill(port)
  await page.getByRole('textbox', { name: 'To port (optional)', exact: true }).fill(portTo)
  await page.getByRole('button', { name: 'Open the port' }).click()
}

/**
 * Asks for a rule's removal and confirms the lockout question every removal raises.
 * @param page The Playwright page under test.
 * @param summary The one-line summary the row's action menu is named after.
 * @returns Resolves once the removal has been confirmed.
 */
const removeRule = async (page: Page, summary: string): Promise<void> => {
  await page.getByRole('button', { name: `Actions for ${summary}` }).click()
  await page.getByRole('menuitem', { name: 'Remove' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Remove the rule' }).click()
}

// The upper bound has to survive the whole way: typed, sent as its own field, and read back onto
// the screen as both ends of the range. A test that asserted only that a POST happened would pass
// against a form that dropped the second field entirely.
test('a rule that names a range sends both bounds, and the table shows both of them', async ({
  page,
}) => {
  const changes = await openScreen(page, [])

  await openPorts(page, '30000', '30099')

  await expect
    .poll(() => {
      return changes
    })
    .toHaveLength(1)
  expect(changes[0]).toEqual({
    method: 'POST',
    body: { port: 30000, portTo: 30099, protocol: 'tcp', sourceCidr: '0.0.0.0/0' },
    query: {},
  })

  // The ports cell's whole text, not containment: `30000` is contained in `30000-30099`, so a
  // containment assertion would pass against exactly the collapse this test exists to catch.
  const row = page.getByRole('row').filter({ hasText: '30000-30099' })
  await expect(row.getByRole('cell').first()).toHaveText('30000-30099')
})

// The discriminator for the test above, and the backwards-compatibility case in the direction that
// reaches this screen: a rule with no upper bound is what every rule written before ranges existed
// is, and what every rule an older agent reports is. It must send no bound at all — not a zero, not
// the port repeated — and read back as the bare port.
test('a rule that names one port sends no bound at all, and the table shows the bare port', async ({
  page,
}) => {
  const changes = await openScreen(page, [])

  await openPorts(page, '8080', '')

  await expect
    .poll(() => {
      return changes
    })
    .toHaveLength(1)
  expect(changes[0]).toEqual({
    method: 'POST',
    body: { port: 8080, portTo: null, protocol: 'tcp', sourceCidr: '0.0.0.0/0' },
    query: {},
  })

  const row = page.getByRole('row').filter({ hasText: '8080' })
  await expect(row.getByRole('cell').first()).toHaveText('8080')
})

// An inverted pair is refused by `nft` itself, and because an apply is one transaction its refusal
// aborts the whole ruleset load: the host keeps its previous policy and the operator is told only
// what `nft` said. The screen names the actual mistake instead, and sends nothing.
test('a range that ends below where it starts is refused before anything is sent', async ({
  page,
}) => {
  const changes = await openScreen(page, [])

  await openPorts(page, '30099', '30000')

  await expect(page.getByText('Leave empty for a single port')).toBeVisible()
  expect(changes).toEqual([])
})

// The half `nft` would ACCEPT, which is why it is refused here as well: `30000-30000` is a second
// spelling of the single port 30000, and a rule has no identity beyond its own value — a later
// removal naming that port would match nothing and report success while the port stayed open.
test('a range whose two ends are the same port is refused too', async ({ page }) => {
  const changes = await openScreen(page, [])

  await openPorts(page, '30000', '30000')

  await expect(page.getByText('Leave empty for a single port')).toBeVisible()
  expect(changes).toEqual([])
})

// A rule is matched by its whole value, so the removal has to carry the bound back exactly as the
// listing reported it. A removal that named only the lower bound would match nothing on the host
// and still report success, and the range would stay open behind a row that had vanished.
test('removing a range sends the bound back, and removing the single port beside it sends none', async ({
  page,
}) => {
  const changes = await openScreen(page, [PASSIVE_RANGE, CONTROL_PORT])

  await removeRule(page, 'tcp/30000-30099 from 0.0.0.0/0')

  await expect
    .poll(() => {
      return changes
    })
    .toHaveLength(1)
  expect(changes[0]).toEqual({
    method: 'DELETE',
    body: null,
    query: { port: '30000', portTo: '30099', protocol: 'tcp', sourceCidr: '0.0.0.0/0' },
  })
  await expect(page.getByRole('row').filter({ hasText: '30000-30099' })).toHaveCount(0)

  await removeRule(page, 'tcp/21 from 0.0.0.0/0')

  await expect
    .poll(() => {
      return changes
    })
    .toHaveLength(2)
  // No `portTo` key at all, and not an empty one: an empty `portTo=` binds as a zero on the panel,
  // and a rule matched by its whole value would then match nothing.
  expect(changes[1]).toEqual({
    method: 'DELETE',
    body: null,
    query: { port: '21', protocol: 'tcp', sourceCidr: '0.0.0.0/0' },
  })
})
