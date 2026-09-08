// Every message key the SPA ships must be reachable from the code, and every key the code asks
// for must exist. Neither direction fails at runtime: vue-i18n renders the key itself when it is
// missing, so a call site pointing at a deleted key ships a screen reading `common.cancel` where
// a button label belongs, and an unreferenced key is dead copy three translators keep in step for
// nothing. `maran structure` already compares the three locale trees against each other
// (rules/vue.md, "Every locale carries the same keys"); what it cannot see is the code, which is
// what this check reads.
//
// Locale key parity between en/ru/hy is deliberately NOT repeated here — one owner per rule.

import { readFileSync, readdirSync } from 'node:fs'
import { join, relative } from 'node:path'
import process from 'node:process'

/** The locale whose keys are the reference set, because the keys are written in English. */
const REFERENCE_LOCALE = 'en'

/** Repository-relative root of the SPA, resolved from this file's own location. */
const FRONTEND_ROOT = new URL('..', import.meta.url).pathname

/** Directories, relative to the SPA root, whose source is scanned for `t(…)` call sites. */
const SOURCE_ROOTS = ['src', 'e2e']

/** File extensions that may contain a `t(…)` call site. */
const SOURCE_EXTENSIONS = ['.vue', '.ts', '.mts']

/**
 * Flattens a nested message bundle into dotted key paths.
 * @param {Record<string, unknown>} bundle The bundle, or a nested part of one.
 * @param {string} prefix The dotted path of `bundle` itself, empty at the top level.
 * @returns {string[]} Every leaf key path in the bundle.
 */
const flatten = (bundle, prefix = '') => {
  const keys = []
  for (const [key, value] of Object.entries(bundle)) {
    const path = prefix ? `${prefix}.${key}` : key
    if (value !== null && typeof value === 'object') {
      keys.push(...flatten(/** @type {Record<string, unknown>} */ (value), path))
    } else {
      keys.push(path)
    }
  }
  return keys
}

/**
 * Lists every source file under a directory that may hold a `t(…)` call site.
 * @param {string} directory An absolute directory path.
 * @returns {string[]} Absolute paths of the source files found beneath it.
 */
const sourceFiles = (directory) => {
  const found = []
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const path = join(directory, entry.name)
    if (entry.isDirectory()) {
      found.push(...sourceFiles(path))
    } else if (SOURCE_EXTENSIONS.some((extension) => entry.name.endsWith(extension))) {
      found.push(path)
    }
  }
  return found
}

/** A `t('a.b.c')` call site: a whole key, quoted, with no interpolation. */
const STATIC_KEY = /\bt\(\s*(['"])([A-Za-z][\w.]*)\1/g

/**
 * Any quoted, dotted identifier anywhere in the source. Used only to decide that a key IS used:
 * a key reaches `t` by more routes than a literal argument — `useNavigation` stores `labelKey`
 * and the shell header maps a route name to one — and a check that only understood one route
 * would report live copy as dead.
 */
const KEY_SHAPED_LITERAL = /(['"])([A-Za-z][\w]*(?:\.[\w]+)+)\1/g

/**
 * A `${'`'}a.b.${'$'}{x}${'`'}` key built from a template literal. The key is only known at runtime, so the literal head is kept as a
 * prefix and every key beneath it counts as referenced — the alternative is a check that reports
 * every dynamically chosen key as an orphan, which is a check nobody can leave switched on.
 */
const DYNAMIC_PREFIX = /`([A-Za-z][\w]*(?:\.[\w]+)+\.)\$\{/g

/**
 * Runs both directions of the check and reports every problem found.
 * @returns {number} The process exit code: 0 when the code and the messages agree.
 */
const main = () => {
  const localeDirectory = join(FRONTEND_ROOT, 'src', 'locales', REFERENCE_LOCALE)
  const declared = new Set()
  for (const file of readdirSync(localeDirectory).filter((name) => name.endsWith('.json'))) {
    for (const key of flatten(JSON.parse(readFileSync(join(localeDirectory, file), 'utf8')))) {
      declared.add(key)
    }
  }

  const problems = []
  const referenced = new Set()
  const mentioned = new Set()
  const prefixes = new Set()
  for (const root of SOURCE_ROOTS) {
    for (const file of sourceFiles(join(FRONTEND_ROOT, root))) {
      const text = readFileSync(file, 'utf8')
      const where = relative(FRONTEND_ROOT, file)
      for (const match of text.matchAll(STATIC_KEY)) {
        referenced.add(match[2])
        if (!declared.has(match[2])) {
          problems.push(`${where}: t('${match[2]}') names a key no locale file declares`)
        }
      }
      for (const match of text.matchAll(KEY_SHAPED_LITERAL)) {
        mentioned.add(match[2])
      }
      for (const match of text.matchAll(DYNAMIC_PREFIX)) {
        prefixes.add(match[1])
      }
    }
  }

  for (const key of [...declared].sort()) {
    const covered =
      mentioned.has(key) || [...prefixes].some((prefix) => key.startsWith(prefix))
    if (!covered) {
      problems.push(`src/locales/${REFERENCE_LOCALE}: key \`${key}\` is declared and never used`)
    }
  }

  for (const problem of problems) {
    process.stdout.write(`${problem}\n`)
  }
  process.stdout.write(
    `check-locale-keys: ${declared.size} keys declared, ${referenced.size} named in code, ` +
      `${prefixes.size} dynamic prefixes, ${problems.length} problems\n`,
  )
  return problems.length === 0 ? 0 : 1
}

process.exitCode = main()
