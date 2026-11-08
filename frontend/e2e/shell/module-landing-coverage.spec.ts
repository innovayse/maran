import { readFileSync, readdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join, resolve } from 'node:path'
import { expect, test } from '@playwright/test'
import { MODULES_WITHOUT_ITEM, NAVIGATION_TREE } from '../../src/router/navigationTree'
import type { PanelModule } from '../../src/types/module'
import type { NavigationPageSpec } from '../../src/types/navigation'
import { stubModules } from '../fixtures/stub-modules-route'
import { stubHealthy } from '../fixtures/stub-health-route'
import { stubSignedIn } from '../fixtures/stub-auth-routes'
import { stubSmtpSettings, storedSmtpSettings } from '../fixtures/stub-smtp-routes'
import { setPersistedLocale } from '../fixtures/set-locale'

// The class of defect this file closes, rather than the one instance of it.
//
// Three times now a module the licence INCLUDES has been linked to the upgrade wall — `identity`,
// `ssl`, and then `notifications`, which was still walled after the fix that named the first two,
// telling an operator the module was outside their licence and, one line below, that its tier was
// "included in the distribution". Each time the repair was an entry in `LANDING_ROUTES`, and each
// time nothing stopped the next module from being forgotten: the map is written in the SPA, the
// catalogue is composed in the backend, and no artefact related the two.
//
// Since issue #311 the sidebar is the navigation tree (`router/navigationTree.ts`), so a composed
// module is covered in exactly one of three ways: the tree lists a built administrator page for it,
// it is named in `MODULES_WITHOUT_ITEM` (its screens live elsewhere on purpose), or — a module the
// tree does not know — it lands on a real screen through the catalogue fallback entry.
//
// So this reads the catalogue's OWN source — `ModuleRegistry.All`, which is exactly what
// `ModulesEndpoint.DescribeModules` enumerates, and each module's `Manifest.Id`, which is exactly
// the `name` field the endpoint reports — serves that list to a real browser, and follows the
// sidebar. A module registered on the backend with no landing route fails here on the day it is
// registered, with nobody having remembered anything.
//
// UNOBSERVED HERE: this reads the C# that BUILDS the catalogue, not an HTTP response from a running
// panel. It therefore cannot see a licence making a module `state: 'locked'` (which is what the
// upgrade wall is honestly for), nor a `Manifest.Id` computed at runtime rather than written as a
// literal — the parse below refuses a manifest it cannot read a literal id out of, rather than
// silently dropping the module from the list it checks.

/** Repository root, resolved from this spec's own location rather than from the runner's cwd. */
const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..', '..')

/** The explicit registry of compiled-in modules — the list `GET /api/v1/modules` enumerates. */
const REGISTRY_FILE = join(REPO_ROOT, 'backend', 'src', 'Maran.Host', 'Modules', 'ModuleRegistry.cs')

/** Where a module's own project lives, keyed by the C# type prefix the registry constructs. */
const MODULE_DIR = join(REPO_ROOT, 'backend', 'src', 'Maran.Modules')

/** A `new XModule()` entry in the registry's collection expression. */
const REGISTERED_MODULE = /new\s+(\w+)Module\(\)/g

/** The `Id: "…"` argument of a manifest's construction — the module's stable machine name. */
const MANIFEST_ID = /\bId:\s*"([^"]+)"/

/**
 * A module id no backend composes, used as this check's inverse control: it MUST reach the upgrade
 * wall. Without it a sidebar that had stopped rendering hrefs at all — or a selector that had
 * stopped matching them — would report every real module as fine.
 */
const UNKNOWN_MODULE = 'zz-not-a-real-module'

/** The English locale tree, where the sidebar labels of the tree's pages are resolved. */
const LOCALE_DIR = join(REPO_ROOT, 'frontend', 'src', 'locales', 'en')

/**
 * Merges one locale file's tree into the accumulated one, key by key.
 * @param target The tree merged so far; written in place.
 * @param source One file's tree.
 * @returns Nothing; `target` holds the result.
 */
const mergeInto = (target: Record<string, unknown>, source: Record<string, unknown>): void => {
  for (const [key, value] of Object.entries(source)) {
    const existing = target[key]
    if (typeof value === 'object' && value !== null && typeof existing === 'object' && existing !== null) {
      mergeInto(existing as Record<string, unknown>, value as Record<string, unknown>)
    } else {
      target[key] = value
    }
  }
}

/**
 * Resolves an i18n key to its English text, the way the SPA's message tree holds it.
 * @param key The dotted key.
 * @returns The text.
 */
const englishLabel = (() => {
  const messages: Record<string, unknown> = {}
  for (const file of readdirSync(LOCALE_DIR).filter((name) => {return name.endsWith('.json')})) {
    mergeInto(messages, JSON.parse(readFileSync(join(LOCALE_DIR, file), 'utf8')) as Record<string, unknown>)
  }
  return (key: string): string => {
    const text = key.split('.').reduce<unknown>((node, part) => {
      return typeof node === 'object' && node !== null ? (node as Record<string, unknown>)[part] : undefined
    }, messages)
    expect(typeof text, `no English text for ${key}`).toBe('string')
    return String(text)
  }
})()

/**
 * The built administrator pages the navigation tree lists for a module.
 * @param module The module's machine name.
 * @returns Its pages; empty when the tree does not name the module.
 */
const treePagesOf = (module: string): NavigationPageSpec[] => {
  return NAVIGATION_TREE.flatMap((group) => {
    return group.nodes.flatMap((node): NavigationPageSpec[] => {
      return node.kind === 'link' ? [node] : [...node.children]
    })
  }).filter((entry) => {
    return entry.module === module && entry.availability.state === 'available' && entry.roles.includes('admin')
  })
}

/**
 * Reads the module ids the backend actually composes, in registration order.
 * @returns One machine name per registered module.
 */
const composedModuleIds = (): string[] => {
  const registry = readFileSync(REGISTRY_FILE, 'utf8')
  const prefixes = [...registry.matchAll(REGISTERED_MODULE)].map((match) => {
    return match[1]
  })
  // A registry this parse cannot read is a broken check, not an empty one: an empty list would let
  // every assertion below pass while observing nothing.
  expect(prefixes.length, `no modules parsed out of ${REGISTRY_FILE}`).toBeGreaterThan(0)

  return prefixes.map((prefix) => {
    const manifest = readFileSync(join(MODULE_DIR, prefix, `${prefix}Manifest.cs`), 'utf8')
    const id = MANIFEST_ID.exec(manifest)
    expect(id, `no literal Id in ${prefix}Manifest.cs`).not.toBeNull()
    return id === null ? '' : id[1]
  })
}

test('every module the backend composes has a sidebar destination that is not the upgrade wall', async ({
  page,
}) => {
  const ids = composedModuleIds()
  const exempt = ids.filter((id) => {return MODULES_WITHOUT_ITEM.has(id)})
  const inTree = ids.filter((id) => {return !MODULES_WITHOUT_ITEM.has(id) && treePagesOf(id).length > 0})
  const fallback = ids.filter((id) => {return !MODULES_WITHOUT_ITEM.has(id) && treePagesOf(id).length === 0})
  // Vacuity guard on the axis that can go blind: a tree that stopped naming modules would move every
  // module into the fallback, and an exemption list that swallowed them would check nothing at all.
  expect(inTree.length, 'no composed module is listed in the navigation tree').toBeGreaterThan(0)
  expect(exempt.length).toBeLessThan(ids.length)
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubModules(page, [
    ...ids.map((name): PanelModule => {
      // A distinctive label per module, so a failure names the module rather than a position.
      return { name, displayName: `Module ${name}`, kindDisplayName: 'Core', tier: 'included', state: 'enabled' }
    }),
    { name: UNKNOWN_MODULE, displayName: `Module ${UNKNOWN_MODULE}`, kindDisplayName: 'Core', tier: 'included', state: 'enabled' },
  ])

  await page.goto('/')
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  await expect(nav.locator('[data-nav-section]').first()).toBeVisible()
  // The catalogue has arrived once its unknown module is drawn. The panel's own sections (Security,
  // Settings) are drawn before it, so counting toggles any earlier misses the module-backed ones.
  await expect(nav.getByRole('link', { name: `Module ${UNKNOWN_MODULE}` })).toBeVisible()

  // Every section's submenu open, so each page the tree lists is drawn as a link of its own.
  const toggles = nav.getByRole('button', { name: /^Show the .+ pages$/ })
  const toggleCount = await toggles.count()
  expect(toggleCount).toBeGreaterThan(0)
  for (let index = 0; index < toggleCount; index++) {
    const toggle = toggles.nth(index)
    if ((await toggle.getAttribute('aria-expanded')) !== 'true') {
      await toggle.click()
    }
    await expect(toggle).toHaveAttribute('aria-expanded', 'true')
  }

  // A module the tree lists is reached through its pages, each a link to a real screen.
  for (const id of inTree) {
    for (const entry of treePagesOf(id)) {
      const link = nav.getByRole('link', { name: englishLabel(entry.labelKey), exact: true })
      await expect(link, `${id}: ${entry.key}`).toHaveCount(1)
      await expect(link, `${id}: ${entry.key}`).not.toHaveAttribute('href', /^\/upgrade\//)
    }
  }
  // A module the tree does not know lands through the catalogue fallback entry, under its own name.
  for (const id of fallback) {
    const link = nav.getByRole('link', { name: `Module ${id}`, exact: true })
    await expect(link, id).toHaveCount(1)
    await expect(link, id).not.toHaveAttribute('href', /^\/upgrade\//)
  }

  // The inverse control: the one module nothing has a screen for DOES reach the wall, which
  // is what proves the assertion below can see an upgrade link at all.
  await expect(page.getByRole('link', { name: `Module ${UNKNOWN_MODULE}` })).toHaveAttribute(
    'href',
    `/upgrade/${UNKNOWN_MODULE}`,
  )

  // And then the property: that link is the ONLY one. The value, not a bound — a real module
  // reaching the wall shows up here as a second href, named in the failure.
  const walled = await nav.locator('a[href^="/upgrade/"]').evaluateAll((links) => {
    return links.map((link) => {
      return link.getAttribute('href')
    })
  })
  expect(walled).toEqual([`/upgrade/${UNKNOWN_MODULE}`])
})

test('the notifications module reaches the outgoing-mail screen from the Settings section in Russian', async ({
  page,
}) => {
  // The instance the class was found through, asserted where it was seen: a Russian panel, whose
  // sidebar entry «Уведомления» led to a wall reading "not included in your licence" directly above
  // "Licence tier: included in the distribution".
  await setPersistedLocale(page, 'ru')
  await stubSignedIn(page)
  await stubHealthy(page)
  await stubSmtpSettings(page, storedSmtpSettings)
  await stubModules(page, [
    {
      name: 'notifications',
      displayName: 'Уведомления',
      tierDisplayName: 'Входит в поставку',
      kindDisplayName: 'Core',
      tier: 'included',
      state: 'enabled',
    },
  ])

  await page.goto('/')
  // The entry is named for its destination (issue #209), so it reads «Исходящая почта» rather
  // than the module's own «Уведомления», and it sits under Settings (issue #311).
  const nav = page.getByRole('navigation', { name: 'Основная навигация' })
  await nav.getByRole('button', { name: 'Показать разделы «Настройки»' }).click()
  await nav.getByRole('link', { name: 'Исходящая почта', exact: true }).click()

  // The destination, not the fact that a navigation happened: an href that merely is not
  // `/upgrade/...` could still name a route nothing serves.
  await expect(page).toHaveURL('/settings/smtp')
  await expect(page.getByRole('heading', { level: 1, name: 'Исходящая почта' })).toBeVisible()
})
