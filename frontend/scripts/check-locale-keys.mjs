// Every message key the SPA ships must be reachable from the code, and every key the code asks
// for must exist. Neither direction fails at runtime: vue-i18n renders the key itself when it is
// missing, so a call site pointing at a deleted key ships a screen reading `common.cancel` where
// a button label belongs, and an unreferenced key is dead copy three translators keep in step for
// nothing. `maran structure` already compares the three locale trees against each other
// (rules/vue.md, "Every locale carries the same keys"); what it cannot see is the code, which is
// what this check reads.
//
// Locale key parity between en/ru/hy is deliberately NOT repeated here — one owner per rule.
//
// COMMENTS ARE NOT CODE, and this check used to be unable to tell the difference. A Playwright
// spec explaining, in prose, why an assertion needs `exact: true` named the key the badge builds;
// the check read the sentence as a call site and reported the branch red for a key nothing calls.
// A checker that punishes engineers for writing comments carefully is a checker that gets worked
// around, so the fix is here rather than in the sentence.
//
// The comment ranges come from TypeScript's own parser — the compiler this repository already
// pins and already runs as `npm run typecheck` — and NOT from a regex over `//` and `/* */`. A
// hand-rolled stripper is wrong in exactly the cases that matter: `//` inside a string literal, a
// URL inside a template literal, a `//` inside a regex literal. This repository has already paid
// for a hand-rolled parser once — a certificate-subject splitter that a comma defeated destroyed a
// customer's private key — and a mis-drawn comment range here fails in the dangerous direction: it
// hides a real call site, and the check then reports agreement it never observed. What the parser
// can and cannot see is printed at the end of every run, and the planted-fixture self-test below
// runs before any measurement.

import ts from 'typescript'
import { readFileSync, readdirSync } from 'node:fs'
import { join, relative } from 'node:path'
import process from 'node:process'

/** The locale whose keys are the reference set, because the keys are written in English. */
const REFERENCE_LOCALE = 'en'

/** Repository-relative root of the SPA, resolved from this file's own location. */
const FRONTEND_ROOT = new URL('..', import.meta.url).pathname

/** The directory holding the reference locale's bundles — the declared side of the comparison. */
const LOCALE_ROOT = join(FRONTEND_ROOT, 'src', 'locales', REFERENCE_LOCALE)

/** Directories, relative to the SPA root, whose source is scanned for `t(…)` call sites. */
const SOURCE_ROOTS = ['src', 'e2e']

/** File extensions that may contain a `t(…)` call site. */
const SOURCE_EXTENSIONS = ['.vue', '.ts', '.mts']

/** A single-file component's script block: the region of a `.vue` file that is TypeScript. */
const SCRIPT_BLOCK = /<script\b[^>]*>([\s\S]*?)<\/script>/gi

/** An HTML comment. The only comment form a `.vue` template region can carry. */
const HTML_COMMENT = /<!--[\s\S]*?-->/g

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
 * A key built from a template literal, whose tail is only known at runtime. The literal head is
 * kept as a prefix and every key beneath it counts as referenced — the alternative is a check that
 * reports every dynamically chosen key as an orphan, which is a check nobody can leave switched on.
 */
const DYNAMIC_PREFIX = /`([A-Za-z][\w]*(?:\.[\w]+)+\.)\$\{/g

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

/**
 * Comment ranges in a TypeScript source, computed by the TypeScript parser rather than by pattern.
 * The parser is asked for the leading trivia of every token it produced, which is where a comment
 * lives; because the token boundaries are the compiler's own, a `//` inside a string, a template
 * literal or a regex literal is never mistaken for the start of a comment.
 * @param {string} text The TypeScript source.
 * @param {number} offset Added to every returned position, for a region inside a larger file.
 * @returns {{start: number, end: number}[]} Every comment range, in no particular order.
 */
const typeScriptCommentRanges = (text, offset = 0) => {
  const source = ts.createSourceFile('scan.ts', text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  /** Comment start -> comment end, keyed by start so a token reached twice contributes once. */
  const seen = new Map()
  /**
   * Records the comments in one node's leading and trailing trivia, then descends into its
   * children. Both are needed: TypeScript classes a comment sitting on the same line as the
   * preceding token as TRAILING trivia and leaves it out of the leading ranges, so a check that
   * asked only for leading trivia would read `t('a.b') // t('c.d')` as two call sites. The
   * planted-fixture self-test below found exactly that while this function was being written.
   * @param {ts.Node} node The node to read.
   * @returns {void}
   */
  const visit = (node) => {
    for (const range of ts.getLeadingCommentRanges(text, node.getFullStart()) ?? []) {
      seen.set(range.pos, range.end)
    }
    for (const range of ts.getTrailingCommentRanges(text, node.getEnd()) ?? []) {
      seen.set(range.pos, range.end)
    }
    // getChildren, not forEachChild: punctuation tokens are children too, and a comment can sit in
    // the trivia of any of them — `t(/* here */ 'a.b')` hangs off the argument's own token.
    for (const child of node.getChildren(source)) {
      visit(child)
    }
  }
  visit(source)
  return [...seen].map(([start, end]) => ({ start: start + offset, end: end + offset }))
}

/**
 * Comment ranges in a single-file component. Each `<script>` block is handed to the TypeScript
 * parser at its own offset; everything outside those blocks is markup, whose only comment form is
 * `<!-- … -->`.
 * @param {string} text The `.vue` source.
 * @returns {{start: number, end: number}[]} Every comment range found.
 */
const vueCommentRanges = (text) => {
  const ranges = []
  /** The script regions, so the markup pass can leave them to the parser. */
  const scripts = []
  for (const match of text.matchAll(SCRIPT_BLOCK)) {
    const body = match[1]
    const start = match.index + match[0].indexOf(body, match[0].indexOf('>'))
    scripts.push({ start, end: start + body.length })
    ranges.push(...typeScriptCommentRanges(body, start))
  }
  for (const match of text.matchAll(HTML_COMMENT)) {
    const inScript = scripts.some(
      (script) => match.index >= script.start && match.index < script.end,
    )
    if (!inScript) {
      ranges.push({ start: match.index, end: match.index + match[0].length })
    }
  }
  return ranges
}

/**
 * Comment ranges in a source file, chosen by extension.
 * @param {string} text The file's text.
 * @param {string} file The file's path, read only for its extension.
 * @returns {{start: number, end: number}[]} Every comment range found.
 */
const commentRanges = (text, file) =>
  file.endsWith('.vue') ? vueCommentRanges(text) : typeScriptCommentRanges(text)

/**
 * Every match of a pattern whose start lies outside every comment range.
 * @param {string} text The text to search.
 * @param {RegExp} pattern A global pattern.
 * @param {{start: number, end: number}[]} ranges The comment ranges to exclude.
 * @returns {{matches: RegExpExecArray[], skipped: number}} The live matches, and how many were
 *   dropped for sitting inside a comment — a number the run prints, so the stripping is visible.
 */
const matchesOutsideComments = (text, pattern, ranges) => {
  const matches = []
  let skipped = 0
  for (const match of text.matchAll(pattern)) {
    if (ranges.some((range) => match.index >= range.start && match.index < range.end)) {
      skipped++
    } else {
      matches.push(match)
    }
  }
  return { matches, skipped }
}

/**
 * A TypeScript fixture planting every case the comment handling has to get right: live call sites,
 * commented ones, and the three shapes that defeat a regex stripper — a `//` inside a string, a
 * `//` inside a template literal, and a `//` inside a regex literal.
 */
const SELF_TEST_TYPESCRIPT = [
  "const live = t('fixture.live')",
  "// t('fixture.line_commented')",
  "/* t('fixture.block_commented') */",
  "const url = 'https://example.test//path'",
  "const second = t('fixture.after_string_slashes')",
  'const template = `https://example.test//${url}`',
  "const third = t('fixture.after_template')",
  'const pattern = /a\\/\\/b/',
  "const fourth = t('fixture.after_regex')",
  "const fifth = t('fixture.with_trailing') // t('fixture.inside_trailing')",
  '/**',
  " * A doc comment naming t('fixture.doc_commented') in prose.",
  ' */',
  "const sixth = t('fixture.after_doc')",
  'const dynamic = t(`fixture.dynamic.${live}`)',
  '// const skipped = t(`fixture.commented_dynamic.${live}`)',
].join('\n')

/** A single-file-component fixture: a live template call, an HTML-commented one, and both in script. */
const SELF_TEST_VUE = [
  '<template>',
  "  <p>{{ t('fixture.template_live') }}</p>",
  "  <!-- {{ t('fixture.html_commented') }} -->",
  '</template>',
  '',
  '<script setup lang="ts">',
  "const inScript = t('fixture.script_live')",
  "// t('fixture.script_commented')",
  '</script>',
].join('\n')

/** The static keys the TypeScript fixture must yield — every live one, and nothing commented. */
const SELF_TEST_TYPESCRIPT_KEYS = [
  'fixture.live',
  'fixture.after_string_slashes',
  'fixture.after_template',
  'fixture.after_regex',
  'fixture.with_trailing',
  'fixture.after_doc',
]

/** The static keys the component fixture must yield. */
const SELF_TEST_VUE_KEYS = ['fixture.template_live', 'fixture.script_live']

/**
 * Runs the extraction over the planted fixtures and reports whether it saw exactly what was
 * planted. This is a positive control, not a smoke test: it asserts the live call sites are FOUND,
 * because the failure this check has to survive is comment handling that quietly swallows code.
 * It runs before anything is measured, and a mismatch aborts the run rather than colouring it.
 * @returns {string[]} The mismatches found; empty when the extraction agrees with the fixtures.
 */
const selfTest = () => {
  const failures = []
  /**
   * Compares the static keys extracted from one fixture against the keys planted in it.
   * @param {string} label The fixture's name, for the failure message.
   * @param {string} text The fixture source.
   * @param {string} file A file name, read only for its extension.
   * @param {string[]} expected The keys the extraction must return, in any order.
   * @returns {void}
   */
  const expectKeys = (label, text, file, expected) => {
    const ranges = commentRanges(text, file)
    const found = matchesOutsideComments(text, STATIC_KEY, ranges).matches.map((match) => match[2])
    const missing = expected.filter((key) => !found.includes(key))
    const extra = found.filter((key) => !expected.includes(key))
    if (missing.length > 0) {
      failures.push(`${label}: planted call site(s) NOT found: ${missing.join(', ')}`)
    }
    if (extra.length > 0) {
      failures.push(`${label}: commented text read as a call site: ${extra.join(', ')}`)
    }
  }

  expectKeys('self-test/typescript', SELF_TEST_TYPESCRIPT, 'fixture.ts', SELF_TEST_TYPESCRIPT_KEYS)
  expectKeys('self-test/vue', SELF_TEST_VUE, 'fixture.vue', SELF_TEST_VUE_KEYS)

  // The orphan direction runs through the same filter, so it gets its own control: a key named
  // only in prose must NOT keep a declared key alive.
  const ranges = commentRanges(SELF_TEST_TYPESCRIPT, 'fixture.ts')
  const mentioned = matchesOutsideComments(SELF_TEST_TYPESCRIPT, KEY_SHAPED_LITERAL, ranges).matches
    .map((match) => match[2])
  if (mentioned.includes('fixture.doc_commented')) {
    failures.push('self-test/typescript: a key named only in a doc comment counted as a mention')
  }
  if (!mentioned.includes('fixture.live')) {
    failures.push('self-test/typescript: a key named in live code did not count as a mention')
  }

  const prefixes = matchesOutsideComments(SELF_TEST_TYPESCRIPT, DYNAMIC_PREFIX, ranges).matches.map(
    (match) => match[1],
  )
  if (!prefixes.includes('fixture.dynamic.')) {
    failures.push('self-test/typescript: the planted dynamic prefix was not found')
  }
  if (prefixes.includes('fixture.commented_dynamic.')) {
    failures.push('self-test/typescript: a commented-out dynamic prefix counted as a prefix')
  }

  return failures
}

/**
 * Runs both directions of the check and reports every problem found.
 * @returns {number} The process exit code: 0 when the code and the messages agree.
 */
const main = () => {
  const selfTestFailures = selfTest()
  if (selfTestFailures.length > 0) {
    for (const failure of selfTestFailures) {
      process.stdout.write(`${failure}\n`)
    }
    // Abort rather than measure: an extraction that cannot pass its own fixtures cannot be trusted
    // to have observed the tree, and a green run from it would be a false pass, which is worse than
    // a red one.
    process.stdout.write(
      `check-locale-keys: ABORTED — the extraction failed ${selfTestFailures.length} of its own ` +
        'planted fixtures; nothing was measured\n',
    )
    return 1
  }

  const declared = new Set()
  for (const file of readdirSync(LOCALE_ROOT).filter((name) => name.endsWith('.json'))) {
    for (const key of flatten(JSON.parse(readFileSync(join(LOCALE_ROOT, file), 'utf8')))) {
      declared.add(key)
    }
  }

  const problems = []
  const referenced = new Set()
  const mentioned = new Set()
  const prefixes = new Set()
  let scanned = 0
  let skippedInComments = 0
  for (const root of SOURCE_ROOTS) {
    for (const file of sourceFiles(join(FRONTEND_ROOT, root))) {
      const text = readFileSync(file, 'utf8')
      const where = relative(FRONTEND_ROOT, file)
      const ranges = commentRanges(text, file)
      scanned++

      const statics = matchesOutsideComments(text, STATIC_KEY, ranges)
      skippedInComments += statics.skipped
      for (const match of statics.matches) {
        referenced.add(match[2])
        if (!declared.has(match[2])) {
          problems.push(`${where}: t('${match[2]}') names a key no locale file declares`)
        }
      }

      const literals = matchesOutsideComments(text, KEY_SHAPED_LITERAL, ranges)
      skippedInComments += literals.skipped
      for (const match of literals.matches) {
        mentioned.add(match[2])
      }

      const dynamic = matchesOutsideComments(text, DYNAMIC_PREFIX, ranges)
      skippedInComments += dynamic.skipped
      for (const match of dynamic.matches) {
        prefixes.add(match[1])
      }
    }
  }

  for (const key of [...declared].sort()) {
    const covered = mentioned.has(key) || [...prefixes].some((prefix) => key.startsWith(prefix))
    if (!covered) {
      problems.push(`src/locales/${REFERENCE_LOCALE}: key \`${key}\` is declared and never used`)
    }
  }

  // Vacuity guards, each on an axis that can go blind while the run still prints "0 problems": a
  // moved directory or a drifted extension list (no files), an emptied locale tree (nothing
  // declared), and — the axis this check gained with comment handling — comment ranges that
  // swallowed the code, which would silence both directions at once and read as agreement.
  if (scanned === 0) {
    problems.push(`${SOURCE_ROOTS.join(', ')}: no source file was scanned — this check measured nothing`)
  }
  if (declared.size === 0) {
    problems.push(`src/locales/${REFERENCE_LOCALE}: no key declared — this check measured nothing`)
  }
  if (referenced.size === 0) {
    problems.push("src, e2e: no `t('…')` call site survived the comment filter — check the parser")
  }
  if (mentioned.size === 0) {
    problems.push('src, e2e: no key-shaped literal survived the comment filter — check the parser')
  }

  for (const problem of problems) {
    process.stdout.write(`${problem}\n`)
  }
  process.stdout.write(
    `check-locale-keys: ${scanned} files scanned, ${declared.size} keys declared, ` +
      `${referenced.size} named in code, ${prefixes.size} dynamic prefixes, ` +
      `${skippedInComments} matches ignored inside comments, ${problems.length} problems\n`,
  )
  process.stdout.write(
    'UNOBSERVED HERE — a key assembled by anything but a template-literal head (string ' +
      'concatenation, a lookup table, `t(variable)`) is invisible in both directions; a ' +
      'key-shaped literal that never reaches `t` still counts as a use, so the orphan direction ' +
      'is deliberately generous; in a `.vue` file only the `<script>` blocks are parsed as ' +
      'TypeScript, so outside them just `<!-- … -->` is recognized and a comment inside a ' +
      '`<style>` block is not; and this reads the reference locale only — the en/ru/hy trees are ' +
      'compared by `maran structure`, not here.\n',
  )
  return problems.length === 0 ? 0 : 1
}

process.exitCode = main()
