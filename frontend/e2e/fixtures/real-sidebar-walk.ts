import { expect, type Locator, type Page } from '@playwright/test'

/** The sidebar's navigation landmark. */
const mainNavigation = (page: Page): Locator => {
  return page.getByRole('navigation', { name: 'Main navigation' })
}

/**
 * Opens every collapsed section of the sidebar by its toggle.
 * @param page The page under test.
 * @returns Resolves once every section is open.
 */
const openEverySection = async (page: Page): Promise<void> => {
  const toggles = mainNavigation(page).getByRole('button', { name: /^Show the .* pages$/ })
  const count = await toggles.count()
  for (let index = 0; index < count; index += 1) {
    const toggle = toggles.nth(index)
    if ((await toggle.getAttribute('aria-expanded')) !== 'true') {
      await toggle.click()
    }
  }
}

/**
 * Lists the names of the sidebar's links, sections opened. Read from the page, so the walk follows
 * whatever the signed-in role is offered and a new menu item joins it without an edit here.
 * @param page The page under test.
 * @returns The links' accessible names, in sidebar order, without duplicates.
 */
export const sidebarDestinations = async (page: Page): Promise<string[]> => {
  await openEverySection(page)
  const names = await mainNavigation(page)
    .getByRole('link')
    .evaluateAll((links) => {
      return links.map((link) => {return link.getAttribute('aria-label') ?? (link.textContent ?? '').trim()})
    })
  return [...new Set(names.filter((name) => {return name !== ''}))]
}

/**
 * Clicks one sidebar destination and requires the screen it opens to have its page heading.
 * Navigation is by click only: the real-stack lane never types a URL.
 * @param page The page under test.
 * @param name The link's accessible name.
 * @returns Resolves once the destination's heading is on screen.
 */
export const openDestination = async (page: Page, name: string): Promise<void> => {
  await openEverySection(page)
  await mainNavigation(page).getByRole('link', { name, exact: true }).first().click()
  // One h1 per screen, and it is the page heading (rules/vue.md, Accessibility). A destination that
  // is a "coming soon" page still draws one, so this holds for every link the sidebar offers.
  await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible()
}
