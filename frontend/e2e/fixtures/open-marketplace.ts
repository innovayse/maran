import { expect, type Page } from '@playwright/test'
import type { PanelModule } from '../../src/types/module'
import { FULL_CATALOGUE, openFullShell } from './open-full-shell'
import {
  stubMarketplace,
  type MarketplaceRecorder,
  type MarketplaceWorld,
} from './stub-marketplace-routes'

/** The marketplace module as the administrator's module catalogue lists it. */
export const MARKETPLACE_MODULE: PanelModule = {
  name: 'marketplace',
  displayName: 'Marketplace',
  kindDisplayName: 'Core',
  tier: 'included',
  state: 'enabled',
}

/** The administrator's catalogue with the marketplace in it. */
export const ADMIN_CATALOGUE: PanelModule[] = [...FULL_CATALOGUE, MARKETPLACE_MODULE]

/**
 * Signs an administrator in on a desktop, installs the marketplace stubs for a world, and opens a
 * marketplace address. The page's heading is waited for, so the status read has been made.
 * @param page The Playwright page under test.
 * @param world The state the stubbed marketplace answers from.
 * @param path The address to open; the marketplace list by default.
 * @returns What the stubs recorded.
 */
export const openMarketplace = async (
  page: Page,
  world: MarketplaceWorld,
  path = '/marketplace',
): Promise<MarketplaceRecorder> => {
  // Held in an object: the stubs are installed inside a callback, which a plain `let` would not survive.
  const held: { recorder?: MarketplaceRecorder } = {}
  await openFullShell(page, path, ADMIN_CATALOGUE, async (target: Page): Promise<void> => {
    held.recorder = await stubMarketplace(target, world)
  })
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  if (held.recorder === undefined) {
    throw new Error('The marketplace stubs were not installed.')
  }
  return held.recorder
}
