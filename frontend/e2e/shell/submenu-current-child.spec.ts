import { expect, test } from '@playwright/test'
import { mainNavigation, openFullShell, openSection } from '../fixtures/open-full-shell'

// Issue #209, point 1: siblings share a path prefix (`/backups` and `/backups/schedule`), so the
// router's prefix match lit two submenu children at once. Only the current child is lit; the
// section's own row keeps the prefix match. Reached by clicks, the way a person gets there.

/** The router's active class as a whole class name, the one the nav row's fill is keyed on. */
const LIT = /(^|\s)router-link-active(\s|$)/

/** One submenu child to open, and the siblings that must stay dark beside it. */
interface Case {
  /** The section's name. */
  section: string
  /** The child to click. */
  child: string
  /** Where it leads. */
  path: string
  /** The section's other children. */
  siblings: string[]
}

const CASES: Case[] = [
  { section: 'Backups', child: 'Schedule', path: '/backups/schedule', siblings: ['Backup list', 'Destinations'] },
  { section: 'Backups', child: 'Destinations', path: '/backups/destinations', siblings: ['Backup list', 'Schedule'] },
  { section: 'Sites', child: 'New site', path: '/sites/new', siblings: ['Site list', 'PHP versions'] },
  { section: 'Databases', child: 'Grant repair', path: '/databases/grant-repair', siblings: ['Database list'] },
  { section: 'Accounts', child: 'New account', path: '/accounts/new', siblings: ['Account list', 'Plans'] },
]

for (const { section, child, path, siblings } of CASES) {
  test(`${section} → ${child} lights that child alone`, async ({ page }) => {
    await openFullShell(page)
    const nav = mainNavigation(page)

    await openSection(page, section)
    await nav.getByRole('link', { name: child, exact: true }).click()
    await expect(page).toHaveURL(path)

    const current = nav.getByRole('link', { name: child, exact: true })
    await expect(current).toHaveAttribute('aria-current', 'page')
    await expect(current).toHaveClass(LIT)
    for (const sibling of siblings) {
      await expect(nav.getByRole('link', { name: sibling, exact: true })).not.toHaveClass(LIT)
    }
    await expect(nav.getByRole('link', { name: section, exact: true })).toHaveClass(LIT)
  })
}
