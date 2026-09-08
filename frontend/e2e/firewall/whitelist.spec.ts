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

  // The question is asked by the panel's one confirmation dialog, and it is asserted on the
  // dialog rather than on the row: the row is where the question used to live, and an assertion
  // left there would pass on a page that had lost the confirmation entirely.
  const dialog = page.getByRole('dialog', { name: `Remove the exemption for ${SEEDED.cidr}` })
  await expect(dialog).toBeVisible()
  await expect(dialog).toContainText(
    'Remove this exemption? The automatic bans can reach the range again — and if it is the range you administer from, that includes you.',
  )
  expect(deletes).toEqual([])

  await dialog.getByRole('button', { name: 'Yes, remove it' }).click()

  await expect
    .poll(() => {
      return deletes
    })
    .toHaveLength(1)
  expect(deletes[0]).toContain(`/api/v1/firewall/whitelist/${SEEDED.id}`)
})

/** Enough exemptions to push the shell's scroll container well past its own height. */
const MANY: WhitelistEntry[] = Array.from({ length: 40 }, (_, index) => {
  return {
    id: `00000000-0000-0000-0000-0000000${(index + 100).toString()}`,
    cidr: `203.0.113.${index}/32`,
    note: `Row ${index}`,
    createdAt: '2026-08-01T09:00:00+00:00',
  }
})

/** The last of them: the row whose trigger sits at the very end of the scroll container. */
const LAST: WhitelistEntry = MANY[MANY.length - 1]

// A menu that closes itself is not a cosmetic defect: the row-actions menu is the only way to reach
// the removal, and it was measured dismissing itself on a ONE PIXEL scroll of the shell's container
// — the browser's own scroll-into-view on the freshly focused trigger — roughly one press in five
// under load, on the last row of a long list, where the trigger sits a pixel short of the
// container's end. The fix was to RE-PLACE the panel against a trigger still on screen instead of
// dismissing it. Nothing in this suite simulates a scroll, so the only evidence the fix held was
// the absence of a flake on a path pressed three times per run — which is not evidence.
//
// The geometry this spec sets up, deliberately, rather than hoping the browser produces it:
// a 700px window, 40 exemptions so `<main class="overflow-y-auto">` (src/layouts/DefaultLayout.vue)
// genuinely scrolls, the container parked ONE PIXEL short of its maximum, the last row's menu
// opened there, and then that single pixel spent.
test('a one-pixel scroll under an open row menu moves it instead of closing it', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 700 })
  await openScreen(page, MANY)

  const row = page.getByRole('row').filter({ hasText: LAST.cidr })
  await expect(row).toBeVisible()

  // Park the container one pixel short of its end, which is the position that produced the defect:
  // the trigger is on screen, and exactly one pixel of scroll is still available underneath it.
  const scrolled = await page.evaluate(() => {
    const main = document.querySelector('main')
    if (main === null) {
      return null
    }
    main.scrollTop = main.scrollHeight
    const max = main.scrollTop
    main.scrollTop = max - 1
    return { max, parked: main.scrollTop }
  })
  // Vacuity guard on the axis that can go blind: if the shell ever stops scrolling here, every
  // assertion below would pass while observing nothing at all.
  expect(scrolled).not.toBeNull()
  expect(scrolled?.max).toBeGreaterThan(0)
  expect(scrolled?.parked).toBe((scrolled?.max ?? 0) - 1)

  await row.getByRole('button', { name: `Actions for ${LAST.cidr}` }).click()

  // Positive control: the menu DID open at this geometry. Without it a menu that never opened
  // would read the same as a menu that closed itself, and this spec would be about nothing.
  const remove = page.getByRole('menuitem', { name: 'Remove' })
  await expect(remove).toBeVisible()

  // Spend the pixel. A real scroll event on the shell's container, which is what the browser's own
  // scroll-into-view produced — capture-phase, because a scroll on an element does not bubble.
  const moved = await page.evaluate(() => {
    const main = document.querySelector('main')
    if (main === null) {
      return null
    }
    const before = main.scrollTop
    main.scrollTop = before + 1
    return { before, after: main.scrollTop }
  })
  // The second half of the guard: the provocation actually happened. A scroll that moved nothing
  // cannot dismiss anything, and a spec that asserted survival after it would be decoration.
  expect(moved?.after).toBe((moved?.before ?? 0) + 1)

  // THE CLAIM: the menu the operator just opened is still open, and still does what it is for.
  await expect(remove).toBeVisible()
  await remove.click()
  await expect(page.getByRole('dialog', { name: `Remove the exemption for ${LAST.cidr}` })).toBeVisible()
})

// A confirmation that sends the request on the way OUT is worse than no confirmation: the operator
// answered "no" and the range lost its exemption anyway. The count is what says so — "the dialog
// closed" is true of both outcomes.
test('dismissing the exemption confirmation sends no request, and confirm is not the default answer', async ({
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

  const dialog = page.getByRole('dialog', { name: `Remove the exemption for ${SEEDED.cidr}` })
  await expect(dialog).toBeVisible()

  // Focus is inside the dialog, and NOT on the confirm button: a reflexive Enter on a dialog that
  // appeared unexpectedly must not be the answer "yes".
  const focus = await page.evaluate(() => {
    const panel = document.querySelector('[role="dialog"]')
    const active = document.activeElement
    return {
      inside: panel !== null && active !== null && panel.contains(active),
      text: active?.textContent?.trim() ?? '',
    }
  })
  expect(focus.inside).toBe(true)
  expect(focus.text).not.toEqual('Yes, remove it')

  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).toHaveCount(0)
  expect(deletes).toEqual([])

  // And the exemption is still on the screen it was never removed from.
  await expect(row).toBeVisible()
})
