import { expect, test } from '../fixtures/guarded-test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubShellReads } from '../fixtures/stub-shell-reads'
import { stubFirewall } from '../fixtures/stub-firewall-routes'
import type { FirewallProtocolChoice } from '../../src/types/firewall'
import type { PanelModule } from '../../src/types/module'
import type { Page } from '@playwright/test'

// The rule form's protocol is a closed set the panel produces (#330): the picker lists what
// `GET /api/v1/firewall/rules/protocols` answers, names each entry with the text the panel gave it,
// and sends the chosen value back. The SPA holds no protocol list and no protocol label, so the
// names below are deliberately NOT the ones the panel ships — a form with its own labels would
// show "TCP" and "UDP" and fail every assertion here.

const LICENSED: PanelModule[] = [{ name: 'firewall', displayName: 'Firewall', kindDisplayName: 'Core', tier: 'included', state: 'enabled' }]

/** Protocols named unlike anything the SPA could have in its own locale files. */
const PANEL_PROTOCOLS: FirewallProtocolChoice[] = [
  { protocol: 'tcp', protocolName: 'Stream (TCP, panel wording)' },
  { protocol: 'udp', protocolName: 'Datagram (UDP, panel wording)' },
]

/**
 * Opens the firewall screen as an administrator, with the given protocol list on offer, and records
 * the bodies of the rules the page sends.
 * @param page The Playwright page under test.
 * @param protocols The protocols the stubbed panel offers.
 * @returns The rule bodies sent, newest last.
 */
const openScreen = async (page: Page, protocols: FirewallProtocolChoice[]): Promise<unknown[]> => {
  const sent: unknown[] = []
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubShellReads(page)
  await stubFirewall(page, { rules: [], bans: [], whitelist: [], protocols })
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().includes('/api/v1/firewall/rules')) {
      sent.push(request.postDataJSON())
    }
  })
  await page.goto('/firewall')
  return sent
}

test('the protocol picker offers what the panel listed, under the names the panel gave', async ({ page }) => {
  await openScreen(page, PANEL_PROTOCOLS)

  const picker = page.getByRole('combobox', { name: 'Protocol' })
  // The first of the panel's choices is selected without a click: the SPA names none itself.
  await expect(picker).toContainText('Stream (TCP, panel wording)')

  await picker.click()
  await expect(page.getByRole('option')).toHaveText(['Stream (TCP, panel wording)', 'Datagram (UDP, panel wording)'])
})

test('choosing the second protocol by click sends that protocol and the table names it as the panel does', async ({
  page,
}) => {
  const sent = await openScreen(page, PANEL_PROTOCOLS)

  await page.getByRole('combobox', { name: 'Protocol' }).click()
  await page.getByRole('option', { name: 'Datagram (UDP, panel wording)' }).click()
  await page.getByRole('textbox', { name: 'Port', exact: true }).fill('5353')
  await page.getByRole('button', { name: 'Open the port' }).click()

  await expect
    .poll(() => {
      return sent
    })
    .toEqual([{ port: 5353, portTo: null, protocol: 'udp', sourceCidr: '0.0.0.0/0' }])
  const row = page.getByRole('row').filter({ hasText: '5353' })
  await expect(row).toContainText('Datagram (UDP, panel wording)')
})

test('with no protocol on offer the form asks for one and sends nothing', async ({ page }) => {
  const sent = await openScreen(page, [])

  await page.getByRole('textbox', { name: 'Port', exact: true }).fill('8080')
  await page.getByRole('button', { name: 'Open the port' }).click()

  await expect(page.getByText('Choose a protocol.')).toBeVisible()
  expect(sent).toEqual([])
})
