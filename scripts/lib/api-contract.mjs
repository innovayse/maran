// The panel's HTTP request contract, checked instead of read.
//
// Every mutating endpoint the SPA calls binds a CQRS command directly, and nothing in either
// toolchain ties the TypeScript body type to that command: rename a command member and both builds
// stay green while the field arrives as its default at runtime. This file is the machine that
// notices. It joins the two trees on the one thing both of them state literally — the HTTP method
// and the route path — and then compares, member by member, the command's JSON-visible members
// against the TypeScript type of the body argument at the call site.
//
// The SPA side is resolved with the SPA's own TypeScript checker (`frontend/node_modules`), not by
// reading text: an interface that inherits, a nested object, a union of string literals and an
// optional member all have to be understood the way `vue-tsc` understands them, or the check would
// report on a shape nobody sends.
//
// The backend side is parsed from source. There is no OpenAPI document in this repository and the
// commands do not need one: `rules/csharp.md` gives every command one file, one `public sealed
// record` and a primary constructor, with the server-established members carrying
// `[property: JsonIgnore]`. That regularity is what makes a parser honest here — and it is checked,
// not assumed: a command whose record cannot be parsed is a FAILURE, never a skipped endpoint.
//
// What this file deliberately does not do is generate anything into `frontend/src`. The SPA's
// request interfaces are hand-written and carry the prose that explains each field
// (`rules/vue.md`); replacing them with generated output would trade documentation the reader
// needs for a guarantee this comparison already provides.
//
// Usage (through the dispatcher, which puts the toolchain on PATH):
//   maran api            check
//   maran api --accept   record the rendered inventory as the new baseline

import { readFileSync, writeFileSync, existsSync, readdirSync, statSync } from 'node:fs'
import { join, dirname, basename, relative } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..')
const BACKEND_SRC = join(ROOT, 'backend', 'src')
const SPA_SRC = join(ROOT, 'frontend', 'src')
const API_DIR = join(SPA_SRC, 'composables', 'apis')
const BASELINE = join(ROOT, 'scripts', 'api-contract-baseline.txt')

// A gate that falls over must not read like a gate that passed. An uncaught error would otherwise
// leave a stack trace and exit 1 — the same exit code a legitimate refusal uses — with neither the
// totals line nor the blind-spot line printed, so CI output would differ from a pass only by the
// ABSENCE of something. It is stated instead, in the checker's own voice, on its own exit code.
process.on('uncaughtException', (error) => {
  console.error('api-contract: THE CHECKER FELL OVER — this is NOT a passing contract and NOT a refusal;')
  console.error('api-contract: nothing was verified. Fix the checker, then re-run.')
  console.error(error.stack ?? String(error))
  process.exit(2)
})

/** Verbs that carry a JSON body through `useApi`. `delete` takes no body argument at all. */
const BODY_VERBS = new Set(['post', 'put', 'patch'])

/** Every failure found, as human sentences naming the endpoint and the member. */
const failures = []

/**
 * Records one failure against one endpoint.
 * @param {string} endpoint The verb and path the failure belongs to.
 * @param {string} message What is wrong, naming the member.
 */
const fail = (endpoint, message) => {
  failures.push(`${endpoint}: ${message}`)
}

// ---------------------------------------------------------------------------
// Backend: controllers, commands, enums
// ---------------------------------------------------------------------------

/**
 * Lists every file under a directory tree, skipping build output.
 * @param {string} dir The directory to walk.
 * @param {string[]} out Accumulator.
 * @returns {string[]} Absolute paths of every file found.
 */
const walk = (dir, out = []) => {
  for (const entry of readdirSync(dir)) {
    if (entry === 'bin' || entry === 'obj' || entry === 'node_modules') continue
    const path = join(dir, entry)
    if (statSync(path).isDirectory()) walk(path, out)
    else out.push(path)
  }
  return out
}

const csharpFiles = walk(BACKEND_SRC).filter((f) => f.endsWith('.cs'))

/** Type name -> source text, for every C# file whose name is its type (one type per file). */
const csharpByTypeName = new Map()
for (const file of csharpFiles) csharpByTypeName.set(basename(file, '.cs'), file)

/**
 * Reads the substring between a `(` at `open` and its matching `)`.
 * @param {string} text The source text.
 * @param {number} open Index of the opening parenthesis.
 * @returns {string} The text between the parentheses, exclusive.
 */
const balanced = (text, open) => {
  let depth = 0
  for (let i = open; i < text.length; i += 1) {
    if (text[i] === '(') depth += 1
    else if (text[i] === ')') {
      depth -= 1
      if (depth === 0) return text.slice(open + 1, i)
    }
  }
  throw new Error('unbalanced parenthesis in C# source')
}

/**
 * Splits a parameter list on its top-level commas, so `IReadOnlyList<A, B>` stays one parameter.
 * @param {string} text The parameter list, without its enclosing parentheses.
 * @returns {string[]} One entry per parameter, trimmed; empty for an empty list.
 */
const splitParameters = (text) => {
  const parts = []
  let depth = 0
  let current = ''
  for (const char of text) {
    if (char === '<' || char === '[' || char === '(') depth += 1
    if (char === '>' || char === ']' || char === ')') depth -= 1
    if (char === ',' && depth === 0) {
      parts.push(current)
      current = ''
      continue
    }
    current += char
  }
  parts.push(current)
  return parts.map((p) => p.trim()).filter((p) => p.length > 0)
}

/**
 * Strips comments from C# source so a doc comment cannot be mistaken for a declaration.
 * @param {string} text The source text.
 * @returns {string} The same text with `//` lines and `/* *\/` blocks removed.
 */
const stripComments = (text) =>
  text.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^[ \t]*\/\/.*$/gm, '')

/**
 * Parses the primary constructor of a C# record into its parameters.
 * @param {string} typeName The record's name, which is also its file name.
 * @returns {{name: string, type: string, attributes: string, hasDefault: boolean}[]|null}
 *   The parameters in declaration order, or null when the type is not a record.
 */
const readRecordParameters = (typeName) => {
  const file = csharpByTypeName.get(typeName)
  if (!file) return null
  const text = stripComments(readFileSync(file, 'utf8'))
  const declaration = new RegExp(`\\brecord\\s+${typeName}\\s*\\(`).exec(text)
  if (!declaration) return null
  const open = declaration.index + declaration[0].length - 1
  return splitParameters(balanced(text, open)).map((raw) => {
    const attributes = (raw.match(/\[[^\]]*\]/g) ?? []).join('')
    const rest = raw.replace(/\[[^\]]*\]/g, '').trim()
    const [head, ...tail] = rest.split('=')
    const words = head.trim().split(/\s+/)
    return {
      name: words[words.length - 1],
      type: words.slice(0, -1).join(' '),
      attributes,
      hasDefault: tail.length > 0,
    }
  })
}

/**
 * Reads the member names of a C# enum, in the wire spelling the panel serialises them with.
 *
 * The Host registers `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`, so an enum crosses as
 * its camelCased member name.
 * @param {string} typeName The enum's name, which is also its file name.
 * @returns {string[]|null} The camelCased member names, or null when the type is not an enum.
 */
const readEnumMembers = (typeName) => {
  const file = csharpByTypeName.get(typeName)
  if (!file) return null
  const text = stripComments(readFileSync(file, 'utf8'))
  const declaration = new RegExp(`\\benum\\s+${typeName}\\b[^{]*\\{`).exec(text)
  if (!declaration) return null
  const body = text.slice(declaration.index + declaration[0].length, text.indexOf('}', declaration.index))
  return body
    .split(',')
    .map((entry) => entry.split('=')[0].trim())
    .filter((entry) => /^[A-Za-z_]\w*$/.test(entry))
    .map((entry) => entry[0].toLowerCase() + entry.slice(1))
}

/**
 * Framework enums a command may bind, with the wire values the panel serialises them as.
 *
 * The resolver reaches a type's shape by finding its declaration file in this repository, so a type
 * declared by the framework has no file to find. `SCALARS` below is the same answer for the same
 * problem — `Guid`, `DateTimeOffset` and the numerics are all framework types stated in a table
 * rather than parsed — and this is that table's enum half, not a second mechanism beside it: an
 * enum needs its member names as well as its kind, so it cannot live in a name -> kind map.
 *
 * The values are camelCased for the same reason `readEnumMembers` camelCases parsed ones: the Host
 * registers `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`.
 */
const BCL_ENUMS = new Map([
  ['DayOfWeek', ['sunday', 'monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday']],
])

/** C# scalars and the JSON shape they cross the wire as. */
const SCALARS = new Map([
  ['string', 'string'],
  ['Guid', 'string'],
  ['DateTimeOffset', 'string'],
  ['DateTime', 'string'],
  ['int', 'number'],
  ['long', 'number'],
  ['short', 'number'],
  ['double', 'number'],
  ['float', 'number'],
  ['decimal', 'number'],
  ['bool', 'boolean'],
])

const COLLECTIONS = /^(IReadOnlyList|IReadOnlyCollection|IList|ICollection|List|IEnumerable)<(.+)>$/

/**
 * Turns one C# type into the wire shape a JSON body carries.
 * @param {string} rawType The declared type, possibly nullable or a collection.
 * @param {string} endpoint The endpoint being described, for failure messages.
 * @param {number} depth Recursion depth guard.
 * @returns {{kind: string, nullable: boolean, of?: object, values?: string[], members?: object[]}}
 *   The wire shape.
 */
const csharpWireShape = (rawType, endpoint, depth = 0) => {
  if (depth > 6) throw new Error(`command shape nested deeper than six levels at ${endpoint}`)
  let type = rawType.trim()
  let nullable = false
  if (type.endsWith('?')) {
    nullable = true
    type = type.slice(0, -1).trim()
  }
  const collection = COLLECTIONS.exec(type)
  if (collection) {
    return { kind: 'array', nullable, of: csharpWireShape(collection[2], endpoint, depth + 1) }
  }
  if (type.endsWith('[]')) {
    return { kind: 'array', nullable, of: csharpWireShape(type.slice(0, -2), endpoint, depth + 1) }
  }
  const scalar = SCALARS.get(type)
  if (scalar) return { kind: scalar, nullable }
  const bclEnum = BCL_ENUMS.get(type)
  if (bclEnum) return { kind: 'enum', nullable, values: bclEnum }
  const enumValues = readEnumMembers(type)
  if (enumValues) return { kind: 'enum', nullable, values: enumValues }
  const record = readRecordParameters(type)
  if (record) {
    return { kind: 'object', nullable, members: csharpMembers(record, endpoint, depth + 1) }
  }
  // An unknown type is a THIRD finding, distinct from both agreement and disagreement: the checker
  // cannot say what crosses the wire here, so it says exactly that and refuses. It does not throw —
  // a crash hides every other endpoint's verdict and blocks `--accept` even for unrelated work —
  // and it does not fall back to a permissive shape, which would report agreement it never checked.
  // The failure is recorded here, at the point of resolution, so a member of an unresolvable type
  // can never be quietly resolved away by a later comparison.
  fail(endpoint, `the command member's type '${rawType}' cannot be resolved — it is neither a scalar, ` +
    'an enum or record declared in backend/src, nor a framework type this checker knows. ' +
    'The contract at this member is UNVERIFIED, not agreed; add it to SCALARS or BCL_ENUMS.')
  return { kind: 'unresolved', nullable, typeName: type }
}

/**
 * Turns a record's parameters into the members a JSON body may carry.
 *
 * A member carrying `[property: JsonIgnore]` is server-established — the controller stamps it from
 * the connection or the route — and MUST NOT appear in the SPA's request type at all.
 * @param {{name: string, type: string, attributes: string, hasDefault: boolean}[]} parameters The record parameters.
 * @param {string} endpoint The endpoint being described, for failure messages.
 * @param {number} depth Recursion depth guard.
 * @returns {{name: string, shape: object, optional: boolean, serverSet: boolean}[]} The members.
 */
const csharpMembers = (parameters, endpoint, depth = 0) =>
  parameters.map((parameter) => ({
    name: parameter.name[0].toLowerCase() + parameter.name.slice(1),
    shape: /JsonIgnore/.test(parameter.attributes)
      ? { kind: 'server-set', nullable: false }
      : csharpWireShape(parameter.type, endpoint, depth),
    optional: parameter.hasDefault,
    serverSet: /JsonIgnore/.test(parameter.attributes),
  }))

/**
 * Reads every controller action, with its verb, path and the command it binds from the body.
 * @returns {{verb: string, path: string, command: string|null, file: string}[]} One entry per action.
 */
const readEndpoints = () => {
  const endpoints = []
  for (const file of csharpFiles.filter((f) => f.endsWith('Controller.cs'))) {
    const text = stripComments(readFileSync(file, 'utf8'))
    const route = /\[Route\("([^"]+)"\)\]/.exec(text)
    if (!route) continue
    const attribute = /\[Http(Get|Post|Put|Patch|Delete)(?:\("([^"]*)"\))?\]/g
    let match
    while ((match = attribute.exec(text)) !== null) {
      const signature = text.indexOf('(', text.indexOf('public ', match.index))
      const parameters = balanced(text, signature)
      const body = /\[FromBody\]\s+([A-Za-z_][\w.]*)\s+\w+/.exec(parameters)
      const suffix = match[2] ? `/${match[2]}` : ''
      endpoints.push({
        verb: match[1].toUpperCase(),
        path: `/${route[1]}${suffix}`.replace(/\{[^}]*\}/g, '{}'),
        command: body ? body[1] : null,
        file: relative(ROOT, file),
      })
    }
  }
  return endpoints
}

// ---------------------------------------------------------------------------
// SPA: call sites and the TypeScript type of each body
// ---------------------------------------------------------------------------

const tsModule = join(ROOT, 'frontend', 'node_modules', 'typescript', 'lib', 'typescript.js')
if (!existsSync(tsModule)) {
  console.error('api-contract: frontend/node_modules/typescript is missing — run `npm ci` in frontend/.')
  process.exit(2)
}
const ts = (await import(pathToFileURL(tsModule).href)).default

const apiFiles = readdirSync(API_DIR)
  .filter((f) => f.endsWith('.ts'))
  .map((f) => join(API_DIR, f))

const program = ts.createProgram(apiFiles, {
  target: ts.ScriptTarget.ESNext,
  module: ts.ModuleKind.ESNext,
  moduleResolution: ts.ModuleResolutionKind.Bundler,
  strict: true,
  noEmit: true,
})
const checker = program.getTypeChecker()

/**
 * Resolves the URL a call site passes, replacing every interpolated route id with `{}`.
 *
 * An interpolation that does not follow a `/`, and a literal `?`, both begin a query string: the
 * path ends there and the call is recorded as carrying one.
 * @param {object} node The first argument of the api call.
 * @param {Map<string, string>} constants The file's `const NAME = '…'` string constants.
 * @returns {{path: string, hasQuery: boolean}|null} The path, or null when it cannot be resolved.
 */
const resolvePath = (node, constants) => {
  const append = (state, text) => {
    if (state.done) return state
    const cut = text.indexOf('?')
    if (cut >= 0) return { path: state.path + text.slice(0, cut), hasQuery: true, done: true }
    return { ...state, path: state.path + text }
  }
  let state = { path: '', hasQuery: false, done: false }
  if (ts.isStringLiteral(node)) return { path: node.text, hasQuery: false }
  if (ts.isIdentifier(node)) {
    const value = constants.get(node.text)
    return value === undefined ? null : { path: value, hasQuery: false }
  }
  if (!ts.isTemplateExpression(node)) return null
  state = append(state, node.head.text)
  for (const span of node.templateSpans) {
    if (!state.done) {
      const known = ts.isIdentifier(span.expression) ? constants.get(span.expression.text) : undefined
      if (known !== undefined) state = append(state, known)
      else if (state.path.endsWith('/')) state = append(state, '{}')
      else state = { ...state, hasQuery: true, done: true }
    }
    state = append(state, span.literal.text)
  }
  return { path: state.path, hasQuery: state.hasQuery }
}

/**
 * Describes a TypeScript type in the same vocabulary the C# side is described in.
 * @param {object} type The checker's type.
 * @param {object} node The expression the type was read at, for symbol resolution.
 * @param {string} endpoint The endpoint being described, for failure messages.
 * @param {number} depth Recursion depth guard.
 * @returns {{kind: string, nullable: boolean, of?: object, values?: string[], members?: object[]}} The shape.
 */
const tsWireShape = (type, node, endpoint, depth = 0) => {
  if (depth > 6) throw new Error(`SPA body nested deeper than six levels at ${endpoint}`)
  let nullable = false
  let parts = type.isUnion() ? type.types : [type]
  const isNullish = (t) => (t.flags & (ts.TypeFlags.Null | ts.TypeFlags.Undefined)) !== 0
  if (parts.some(isNullish)) {
    nullable = true
    parts = parts.filter((t) => !isNullish(t))
  }
  if (parts.length > 1 && parts.every((t) => (t.flags & ts.TypeFlags.StringLiteral) !== 0)) {
    return { kind: 'enum', nullable, values: parts.map((t) => t.value).sort() }
  }
  if (parts.length !== 1) {
    // A boolean is modelled as `true | false`; anything else that unions is not a wire shape.
    if (parts.every((t) => (t.flags & ts.TypeFlags.BooleanLiteral) !== 0)) return { kind: 'boolean', nullable }
    throw new Error(`SPA body member at ${endpoint} is a union this check cannot describe: ${checker.typeToString(type)}`)
  }
  const single = parts[0]
  if (single.flags & (ts.TypeFlags.String | ts.TypeFlags.StringLiteral)) return { kind: 'string', nullable }
  if (single.flags & (ts.TypeFlags.Number | ts.TypeFlags.NumberLiteral)) return { kind: 'number', nullable }
  if (single.flags & (ts.TypeFlags.Boolean | ts.TypeFlags.BooleanLiteral)) return { kind: 'boolean', nullable }
  if (checker.isArrayType(single)) {
    const element = checker.getTypeArguments(single)[0]
    return { kind: 'array', nullable, of: tsWireShape(element, node, endpoint, depth + 1) }
  }
  const members = single.getProperties().map((symbol) => {
    const declared = checker.getTypeOfSymbolAtLocation(symbol, symbol.valueDeclaration ?? node)
    return {
      name: symbol.getName(),
      optional: (symbol.getFlags() & ts.SymbolFlags.Optional) !== 0,
      shape: tsWireShape(declared, node, endpoint, depth + 1),
    }
  })
  return { kind: 'object', nullable, members }
}

/**
 * Finds every mutating api call in the SPA's api composables.
 * @returns {{verb: string, path: string, hasQuery: boolean, body: object|null, where: string}[]} The call sites.
 */
const readCallSites = () => {
  const sites = []
  for (const file of apiFiles) {
    const source = program.getSourceFile(file)
    const constants = new Map()
    ts.forEachChild(source, (node) => {
      if (!ts.isVariableStatement(node)) return
      for (const declaration of node.declarationList.declarations) {
        if (ts.isIdentifier(declaration.name) && declaration.initializer && ts.isStringLiteral(declaration.initializer)) {
          constants.set(declaration.name.text, declaration.initializer.text)
        }
      }
    })
    const visit = (node) => {
      if (
        ts.isCallExpression(node) &&
        ts.isPropertyAccessExpression(node.expression) &&
        ts.isIdentifier(node.expression.expression) &&
        node.expression.expression.text === 'api'
      ) {
        const verb = node.expression.name.text
        if (BODY_VERBS.has(verb) || verb === 'delete') {
          const where = `${relative(ROOT, file)}:${source.getLineAndCharacterOfPosition(node.getStart()).line + 1}`
          const url = resolvePath(node.arguments[0], constants)
          if (!url) {
            fail(`${verb.toUpperCase()} (${where})`, 'the request path could not be resolved statically')
          } else {
            const bodyNode = BODY_VERBS.has(verb) ? node.arguments[1] : undefined
            const hasBody = bodyNode !== undefined && bodyNode.kind !== ts.SyntaxKind.UndefinedKeyword &&
              !(ts.isIdentifier(bodyNode) && bodyNode.text === 'undefined')
            sites.push({
              verb: verb.toUpperCase(),
              path: url.path,
              hasQuery: url.hasQuery,
              bodyNode: hasBody ? bodyNode : null,
              where,
            })
          }
        }
      }
      ts.forEachChild(node, visit)
    }
    visit(source)
  }
  return sites
}

// ---------------------------------------------------------------------------
// The comparison
// ---------------------------------------------------------------------------

/**
 * Renders a wire shape as the one-line vocabulary the baseline is written in.
 * @param {object} shape The shape to render.
 * @returns {string} Its text form.
 */
const render = (shape) => {
  const suffix = shape.nullable ? '?' : ''
  if (shape.kind === 'enum') return `enum(${[...shape.values].sort().join('|')})${suffix}`
  if (shape.kind === 'array') return `${render(shape.of)}[]${suffix}`
  if (shape.kind === 'object') return `object${suffix}`
  if (shape.kind === 'unresolved') return `unresolved(${shape.typeName})${suffix}`
  return `${shape.kind}${suffix}`
}

/**
 * Compares one command's members against the SPA's body members, naming every disagreement.
 * @param {string} endpoint The verb and path, for failure messages.
 * @param {object[]} commandMembers The command's members.
 * @param {object[]} bodyMembers The SPA's members.
 * @param {string} prefix The dotted member path reached so far.
 * @param {string[]} lines Accumulator for the rendered inventory.
 */
const compare = (endpoint, commandMembers, bodyMembers, prefix, lines) => {
  const bodyByName = new Map(bodyMembers.map((m) => [m.name, m]))
  for (const member of commandMembers) {
    const name = `${prefix}${member.name}`
    const sent = bodyByName.get(member.name)
    bodyByName.delete(member.name)
    if (member.serverSet) {
      if (sent) {
        fail(endpoint, `'${name}' is server-established ([property: JsonIgnore]) but the SPA's request type declares it`)
      }
      continue
    }
    if (!sent) {
      if (!member.optional) {
        fail(endpoint, `command member '${name}' (${render(member.shape)}) is not sent by the SPA — it would arrive as its default`)
      }
      lines.push(`  ${name}: ${render(member.shape)} [not sent]`)
      continue
    }
    compareShape(endpoint, name, member, sent, lines)
  }
  for (const extra of bodyByName.values()) {
    fail(endpoint, `the SPA sends '${prefix}${extra.name}' (${render(extra.shape)}), which the bound command has no member for`)
  }
}

/**
 * Compares one member's shape on both sides.
 * @param {string} endpoint The verb and path, for failure messages.
 * @param {string} name The dotted member name.
 * @param {object} member The command member.
 * @param {object} sent The SPA member.
 * @param {string[]} lines Accumulator for the rendered inventory.
 */
const compareShape = (endpoint, name, member, sent, lines) => {
  const expected = member.shape
  const actual = sent.shape
  if (expected.kind === 'unresolved') {
    // The disagreement was already recorded where the type failed to resolve. Comparing an
    // unresolvable shape against the SPA's would add a second, misleading line claiming a MISMATCH
    // where the truth is that nothing was checked.
    lines.push(`  ${name}: ${render(expected)}`)
    return
  }
  if (expected.kind !== actual.kind) {
    fail(endpoint, `'${name}' is ${render(expected)} in the command and ${render(actual)} in the SPA`)
    return
  }
  if (expected.nullable && !(actual.nullable || sent.optional)) {
    fail(endpoint, `'${name}' is nullable in the command but required and non-nullable in the SPA`)
  }
  if (!expected.nullable && !member.optional && (actual.nullable || sent.optional)) {
    fail(endpoint, `'${name}' is optional or nullable in the SPA but the command declares it required`)
  }
  if (expected.kind === 'enum') {
    const a = [...expected.values].sort().join('|')
    const b = [...actual.values].sort().join('|')
    if (a !== b) fail(endpoint, `'${name}' enum values differ — command has (${a}), SPA has (${b})`)
  }
  if (expected.kind === 'array') {
    lines.push(`  ${name}: ${render(expected)}`)
    if (expected.of.kind !== actual.of.kind) {
      fail(endpoint, `'${name}' holds ${render(expected.of)} in the command and ${render(actual.of)} in the SPA`)
    } else if (expected.of.kind === 'object') {
      compare(endpoint, expected.of.members, actual.of.members, `${name}[].`, lines)
    } else if (expected.of.kind === 'enum') {
      const a = [...expected.of.values].sort().join('|')
      const b = [...actual.of.values].sort().join('|')
      if (a !== b) fail(endpoint, `'${name}[]' enum values differ — command has (${a}), SPA has (${b})`)
    }
    return
  }
  lines.push(`  ${name}: ${render(expected)}`)
  if (expected.kind === 'object') compare(endpoint, expected.members, actual.members, `${name}.`, lines)
}

const endpoints = readEndpoints()
const sites = readCallSites()

/** Endpoints keyed by verb and path, so a call site can find the action it reaches. */
const byRoute = new Map(endpoints.map((e) => [`${e.verb} ${e.path}`, e]))

const lines = []
const unchecked = []
let checkedEndpoints = 0
let checkedMembers = 0

for (const site of sites.sort((a, b) => `${a.path} ${a.verb}`.localeCompare(`${b.path} ${b.verb}`))) {
  const key = `${site.verb} ${site.path}`
  const endpoint = byRoute.get(key)
  if (!endpoint) {
    fail(`${key} (${site.where})`, 'the SPA calls a path no controller action serves')
    continue
  }
  if (!site.bodyNode) {
    unchecked.push(`${key} — ${site.hasQuery ? 'query-string' : 'route-only'} call, no JSON body`)
    continue
  }
  if (!endpoint.command) {
    fail(key, 'the SPA sends a JSON body but the action binds no [FromBody] command')
    continue
  }
  const parameters = readRecordParameters(endpoint.command)
  if (!parameters) {
    fail(key, `the bound command '${endpoint.command}' could not be parsed from backend/src — the check cannot see this endpoint`)
    continue
  }
  const commandMembers = csharpMembers(parameters, key)
  const bodyType = checker.getTypeAtLocation(site.bodyNode)
  const body = tsWireShape(bodyType, site.bodyNode, key)
  if (body.kind !== 'object') {
    fail(key, `the SPA's body argument is ${render(body)}, not an object`)
    continue
  }
  lines.push(`${key}  ->  ${endpoint.command}`)
  const before = lines.length
  compare(key, commandMembers, body.members, '', lines)
  checkedEndpoints += 1
  checkedMembers += lines.length - before
}

// A parser that has gone blind reports agreement, so the coverage it achieved is part of the
// artefact rather than a number in a log: the baseline below carries it, and a drop is a diff.
const header = [
  '# The panel\'s HTTP request contract, rendered by `maran api` from the two trees it joins.',
  '#',
  '# Left: the verb and path a controller action serves. Right: the CQRS command it binds with',
  '# [FromBody]. Below each: the members a JSON body may carry, in their wire spelling, as agreed',
  '# by the command record AND the TypeScript type the SPA passes at the call site.',
  '#',
  '# WHAT THIS CANNOT SEE: query-string and route binding. A [FromQuery] parameter and a route id',
  '# never reach System.Text.Json, so nothing below describes them; the calls that use them are',
  '# listed under "not covered" and their parameters are checked by nobody. Response bodies are',
  '# also out of scope — this is the REQUEST contract only. Endpoints with no SPA caller cannot be',
  '# joined and are listed too.',
  '#',
  '# Regenerate with `maran api --accept`, in its own reviewed commit.',
  '',
  `covered: ${checkedEndpoints} endpoints, ${checkedMembers} members`,
  '',
]

const uncoveredActions = endpoints
  .filter((e) => !sites.some((s) => `${s.verb} ${s.path}` === `${e.verb} ${e.path}`))
  .map((e) => `${e.verb} ${e.path}${e.command ? ` (binds ${e.command}, no SPA caller)` : ''}`)

const rendered =
  [
    ...header,
    ...lines,
    '',
    '# not covered — no JSON body crosses here, so this check says nothing about it',
    ...unchecked.sort().map((u) => `  ${u}`),
    ...uncoveredActions.sort().map((u) => `  ${u}`),
  ].join('\n') + '\n'

if (process.argv.includes('--accept')) {
  if (failures.length > 0) {
    console.error('api-contract: refusing to record a baseline while the contract disagrees:')
    for (const failure of failures) console.error(`  ${failure}`)
    process.exit(1)
  }
  writeFileSync(BASELINE, rendered)
  console.log(`api-contract: recorded ${checkedEndpoints} endpoints / ${checkedMembers} members into ${relative(ROOT, BASELINE)}`)
  process.exit(0)
}

if (checkedEndpoints === 0) {
  console.error('api-contract: no endpoint was checked at all — the check has gone blind, which is a FAILURE, not a pass.')
  process.exit(1)
}

for (const failure of failures) console.error(`api-contract: ${failure}`)

let exitCode = failures.length > 0 ? 1 : 0

if (!existsSync(BASELINE)) {
  console.error(`api-contract: ${relative(ROOT, BASELINE)} is missing — record it with \`maran api --accept\`.`)
  exitCode = 1
} else if (readFileSync(BASELINE, 'utf8') !== rendered) {
  console.error('api-contract: the rendered contract differs from the recorded baseline.')
  // A positional diff is unreadable here: one dropped member shifts every line after it. The
  // difference is reported as the lines that appeared and the lines that went away, which says the
  // same thing in the number of lines the change actually touched.
  const recorded = readFileSync(BASELINE, 'utf8').split('\n')
  const fresh = rendered.split('\n')
  const missing = recorded.filter((line) => !fresh.includes(line))
  const added = fresh.filter((line) => !recorded.includes(line))
  for (const line of missing.slice(0, 20)) console.error(`  - ${line}`)
  for (const line of added.slice(0, 20)) console.error(`  + ${line}`)
  if (missing.length + added.length > 40) {
    console.error(`  … ${missing.length + added.length - 40} further differing lines`)
  }
  console.error('  If the change is intended, record it with `maran api --accept` in its own commit.')
  exitCode = 1
}

console.log(
  `api-contract: ${checkedEndpoints} endpoints / ${checkedMembers} members checked, ` +
    `${unchecked.length + uncoveredActions.length} not covered (no JSON body), ${failures.length} failures.`,
)
console.log('api-contract: UNOBSERVED HERE — query-string and route binding, and every response body.')
process.exit(exitCode)
