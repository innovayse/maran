import { expect, test, type Locator } from '@playwright/test'
import { mainNavigation, openFullShell, openSection } from '../fixtures/open-full-shell'

// Issue #209, point 5: page eyebrows were written by hand per page and disagreed with the sidebar
// (Tasks said SYSTEM under Overview, the audit journal SECURITY under System, Backups PROTECTION
// under Security, Accounts ACCOUNTS under Hosting). The eyebrow now names the group the sidebar
// draws the entry under, so this walks EVERY entry and compares the two.

/**
 * Names the sidebar group a navigation row is drawn under: the label of the list holding it.
 * @param link The row.
 * @returns The group's label.
 */
const groupOf = async (link: Locator): Promise<string> => {
  return link.evaluate((element) => {
    const list = element.closest('ul[aria-labelledby]')
    const label = list === null ? null : document.getElementById(list.getAttribute('aria-labelledby') ?? '')
    return (label?.textContent ?? '').trim()
  })
}

/** The page eyebrow over the title. */
const EYEBROW = 'main [data-ui="eyebrow"]'

test('every top-level entry opens a page whose eyebrow is the group the sidebar draws it under', async ({
  page,
}) => {
  await openFullShell(page)
  const nav = mainNavigation(page)
  const links = nav.locator('a:not([data-nav-parent])')
  const count = await links.count()
  expect(count).toBeGreaterThan(10)

  for (let index = 0; index < count; index++) {
    const link = links.nth(index)
    const name = (await link.innerText()).trim()
    const group = await groupOf(link)
    expect(group, name).not.toBe('')
    await link.click()
    await expect(page.locator(EYEBROW).first(), name).toHaveText(group)
  }
})

test('the four mismatches the issue named now agree with the sidebar', async ({ page }) => {
  await openFullShell(page)
  const nav = mainNavigation(page)

  await nav.getByRole('link', { name: 'Background tasks', exact: true }).click()
  await expect(page.locator(EYEBROW)).toHaveText('Overview')

  await nav.getByRole('link', { name: 'Audit journal', exact: true }).click()
  await expect(page.locator(EYEBROW)).toHaveText('System')

  await nav.getByRole('link', { name: 'Backups', exact: true }).click()
  await expect(page.locator(EYEBROW)).toHaveText('Security')

  await nav.getByRole('link', { name: 'Accounts', exact: true }).click()
  await expect(page.locator(EYEBROW)).toHaveText('Hosting')
})

test('a submenu page takes its section group, not a word of its own', async ({ page }) => {
  await openFullShell(page)
  const nav = mainNavigation(page)

  await openSection(page, 'Backups')
  await nav.getByRole('link', { name: 'Schedule', exact: true }).click()
  await expect(page.locator(EYEBROW)).toHaveText('Security')

  await openSection(page, 'Databases')
  await nav.getByRole('link', { name: 'Grant repair', exact: true }).click()
  await expect(page.locator(EYEBROW)).toHaveText('Hosting')

  // The firewall's three pages (#196) carry no eyebrow of their own either.
  await openSection(page, 'Firewall')
  for (const child of ['Rules', 'Bans', 'Never banned']) {
    await nav.getByRole('link', { name: child, exact: true }).click()
    await expect(page.locator(EYEBROW), child).toHaveText('Security')
  }
})
