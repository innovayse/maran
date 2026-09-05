import { expect, test, type Page } from '@playwright/test'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubModules } from '../fixtures/stub-modules-route'
import { seededWhitelistNote, stubFirewall } from '../fixtures/stub-firewall-routes'
import type { WhitelistEntry } from '../../src/types/firewall'
import type { PanelModule } from '../../src/types/module'

const LICENSED: PanelModule[] = [
  { name: 'firewall', displayName: 'Firewall', tier: 'included', isEnabled: true },
]

// The row the installer seeds: the address the panel was installed from. Nothing on the wire marks
// it as the panel's own, which is exactly why the note is the column that has to be rendered.
const SEEDED: WhitelistEntry = {
  id: '00000000-0000-0000-0000-0000000000w1',
  cidr: '198.51.100.0/24',
  note: seededWhitelistNote,
  createdAt: '2026-08-01T09:00:00+00:00',
}

/**
 * Puts the firewall screen in front of an administrator with the given exemptions in force, and
 * starts recording the whitelist writes the page sends — the body, not merely that a request
 * happened, because the point of every test here is WHICH range left the browser.
 * @param page The Playwright page under test.
 * @param entries The exemptions the stubbed panel reports.
 * @returns The recorded create bodies, newest last.
 */
const openScreen = async (page: Page, entries: WhitelistEntry[]): Promise<unknown[]> => {
  const creates: unknown[] = []
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, LICENSED)
  await stubFirewall(page, { rules: [], bans: [], whitelist: entries })
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().includes('/api/v1/firewall/whitelist')) {
      creates.push(request.postDataJSON())
    }
  })
  await page.goto('/firewall')
  return creates
}

/**
 * Fills the exemption form and submits it.
 * @param page The Playwright page under test.
 * @param cidr The range to type.
 * @param note The note to type.
 * @returns Resolves once the submit control has been pressed.
 */
const addExemption = async (page: Page, cidr: string, note: string): Promise<void> => {
  // `exact`, because the rule form above has a "Source range" field whose accessible name would
  // otherwise match too.
  await page.getByRole('textbox', { name: 'Range', exact: true }).fill(cidr)
  await page.getByRole('textbox', { name: 'Note', exact: true }).fill(note)
  await page.getByRole('button', { name: 'Add the exemption' }).click()
}

// The IPv4-mapped spelling is the one IPv6 form the panel refuses outright (`CidrRange.IsUsable`),
// because a mapped RANGE stays in the IPv6 family while every address compared against it has been
// mapped down to plain IPv4 — so the row would be stored, read back verbatim, and exempt nobody.
// The client mirrors that refusal rather than sending a range it knows will come back rejected.
test('an IPv4-mapped range is refused here, as the panel refuses it', async ({ page }) => {
  const creates = await openScreen(page, [])

  await addExemption(page, '::ffff:198.51.100.10/128', 'The office')

  await expect(page.getByText('Enter a range in CIDR notation')).toBeVisible()
  expect(creates).toEqual([])
})

// A range carrying bits below its prefix is refused for the panel's own reason: 203.0.113.7/24
// exempts either one machine or two hundred and fifty-six of them, and an exemption must never be
// wider than the person who wrote it believes.
test('a range with host bits below its prefix is refused here too', async ({ page }) => {
  const creates = await openScreen(page, [])

  await addExemption(page, '203.0.113.7/24', 'The office')

  await expect(page.getByText('Enter a range in CIDR notation')).toBeVisible()
  expect(creates).toEqual([])
})

// The discriminator, without which both tests above would pass on a form that refused everything —
// including an ordinary range and an ordinary IPv6 one, which the panel takes.
test('a range the panel accepts is sent exactly as it was typed', async ({ page }) => {
  const creates = await openScreen(page, [])

  await addExemption(page, '2001:db8::/32', 'The office')

  await expect
    .poll(() => {
      return creates
    })
    .toHaveLength(1)
  expect(creates[0]).toEqual({ cidr: '2001:db8::/32', note: 'The office' })
  await expect(page.getByRole('row').filter({ hasText: '2001:db8::/32' })).toBeVisible()
})

// The seeded row is the panel's, not an administrator's, and nothing on the wire says so — the note
// is the only thing that does. So the note has to reach the screen verbatim, and the screen has to
// say what such a row means before an operator removes the range they administer from.
test('the installer-seeded exemption is rendered with the note that explains it', async ({ page }) => {
  await openScreen(page, [SEEDED])

  await expect(page.getByText('the panel adds one row itself')).toBeVisible()
  const row = page.getByRole('row').filter({ hasText: SEEDED.cidr })
  await expect(row).toContainText(seededWhitelistNote)
})

// Removing an exemption lets the automatic bans reach the range again — possibly the operator's own
// — so it is confirmed first, and nothing is sent until it is.
test('removing an exemption asks first, and then names the row the panel gave back', async ({
  page,
}) => {
  const deletes: string[] = []
  await openScreen(page, [SEEDED])
  page.on('request', (request) => {
    if (request.method() === 'DELETE' && request.url().includes('/api/v1/firewall/whitelist')) {
      deletes.push(request.url())
    }
  })

  const row = page.getByRole('row').filter({ hasText: SEEDED.cidr })
  await row.getByRole('button', { name: `Actions for ${SEEDED.cidr}` }).click()
  await page.getByRole('menuitem', { name: 'Remove' }).click()

  await expect(row).toContainText('Remove this exemption?')
  expect(deletes).toEqual([])

  await row.getByRole('button', { name: 'Yes, remove it' }).click()

  await expect
    .poll(() => {
      return deletes
    })
    .toHaveLength(1)
  expect(deletes[0]).toContain(`/api/v1/firewall/whitelist/${SEEDED.id}`)
})
