import { readFileSync, readdirSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import type { Page } from '@playwright/test'

/** The languages whose screens must carry no English chrome. */
export type TranslatedLocale = 'ru' | 'hy'

/** A locale file's nested tree of strings. */
interface LocaleTree {
  [key: string]: string | LocaleTree
}

/** Where the SPA keeps its locale trees, resolved from this file rather than from the runner's cwd. */
const LOCALES_DIR = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..', 'src', 'locales')

/** The shortest English phrase worth looking for; shorter ones collide with names and codes. */
const MINIMUM_LENGTH = 4

/** The shape of an i18n key rendered as text, which is what vue-i18n prints for a key it cannot find. */
const RAW_KEY = /^app(\.[A-Za-z0-9_]+){2,}$/

/**
 * Flattens one locale tree into `dotted.key -> text`.
 * @param tree The nested tree.
 * @param prefix The dotted path of the tree's parent.
 * @param into The map the leaves are added to.
 * @returns Nothing; `into` is filled.
 */
const flatten = (tree: LocaleTree, prefix: string, into: Map<string, string>): void => {
  for (const [key, value] of Object.entries(tree)) {
    const path = prefix === '' ? key : `${prefix}.${key}`
    if (typeof value === 'string') {
      into.set(path, value)
    } else {
      flatten(value, path, into)
    }
  }
}

/**
 * Reads every file of one locale into a single flat map.
 * @param locale The locale folder's name.
 * @returns `dotted.key -> text` for the whole locale.
 */
const readLocale = (locale: 'en' | TranslatedLocale): Map<string, string> => {
  const directory = join(LOCALES_DIR, locale)
  const flat = new Map<string, string>()
  for (const file of readdirSync(directory).filter((name) => {return name.endsWith('.json')})) {
    flatten(JSON.parse(readFileSync(join(directory, file), 'utf8')) as LocaleTree, '', flat)
  }
  return flat
}

/**
 * The English phrases that this locale translates, and that therefore have no business appearing
 * on one of its screens. A phrase is left out when ANY key keeps the English spelling in this locale
 * (an acronym, a product name): the text on screen could then be that key's.
 * @param locale The locale to compare English with.
 * @returns The English phrases that must be gone.
 */
const phrasesThatMustBeTranslated = (locale: TranslatedLocale): Set<string> => {
  const english = readLocale('en')
  const local = readLocale(locale)
  const translated = new Set<string>()
  const kept = new Set<string>()
  for (const [key, text] of english) {
    // A phrase with a placeholder never appears verbatim on a screen.
    if (text.length < MINIMUM_LENGTH || text.includes('{')) {
      continue
    }
    if (local.get(key) === text) {
      kept.add(text)
    } else {
      translated.add(text)
    }
  }
  for (const text of kept) {
    translated.delete(text)
  }
  return translated
}

/**
 * Reads the text a person can read on the page: its visible lines and the accessible names that
 * are not drawn (`aria-label`, `title`, `placeholder`).
 * @param page The page under test.
 * @returns Trimmed, non-empty strings.
 */
const readableText = async (page: Page): Promise<string[]> => {
  const visible = (await page.locator('body').innerText()).split('\n')
  const attributes = await page.locator('[aria-label], [title], [placeholder]').evaluateAll((elements) => {
    return elements.flatMap((element) => {
      return ['aria-label', 'title', 'placeholder'].map((name) => {return element.getAttribute(name) ?? ''})
    })
  })
  return [...visible, ...attributes].map((line) => {return line.trim()}).filter((line) => {return line !== ''})
}

/**
 * Finds English chrome and raw i18n keys on the page the person is looking at, in a language that
 * is not English. A server-produced sentence arrives already translated, so this only compares with
 * the SPA's own locale files.
 *
 * UNOBSERVED HERE: English text the SPA does not own (a user's own data, a log line, a hostname)
 * and English inside a longer sentence. Only a line that equals a translated English phrase, or a
 * raw key, is a finding.
 * @param page The page under test, already switched to the locale.
 * @param locale The language the page is in.
 * @param allowed Lines to leave alone, for data a journey created itself and that happens to read as chrome.
 * @returns One sentence per finding; empty when the page is fully in the language.
 */
export const findUntranslatedText = async (
  page: Page,
  locale: TranslatedLocale,
  allowed: readonly string[] = [],
): Promise<string[]> => {
  const mustBeGone = phrasesThatMustBeTranslated(locale)
  const findings: string[] = []
  for (const line of await readableText(page)) {
    if (allowed.includes(line)) {
      continue
    }
    if (RAW_KEY.test(line)) {
      findings.push(`raw i18n key on a ${locale} page: "${line}"`)
    } else if (mustBeGone.has(line)) {
      findings.push(`English text on a ${locale} page: "${line}"`)
    }
  }
  return findings
}
