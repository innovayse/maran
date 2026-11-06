// The breaking-change detector behind `maran openapi`: a structural diff of two OpenAPI 3 documents.
//
//   node openapi-diff.mjs diff <baseline.json> <generated.json>   print findings, exit 1 on BREAKING
//   node openapi-diff.mjs canon <in.json> <out.json>               write the document in canonical form
//   node openapi-diff.mjs selftest                                 run the planted controls only
//
// WHAT "BREAKING" MEANS HERE. A client written against the baseline stops working against the
// generated document. The comparison is directional, and that is the whole design: a RESPONSE is
// something the server produces, so a client breaks when something it read goes away or changes
// shape; a REQUEST is something the client produces, so a client breaks when the server starts
// demanding something the client never sent. The same schema walk is therefore run twice with the
// direction as a parameter, and a property that is merely new is additive in a response but
// breaking in a request when it is required.
//
// EXIT STATUS. 0 no breaking change, 1 at least one breaking change or a vacuous comparison,
// 2 an input that is not a readable OpenAPI 3 document. The wrapper (openapi.sh) scores the printed
// verdict, not this status.
//
// UNOBSERVED HERE, stated in the output as well:
//   - Runtime behaviour. Only what the document DESCRIBES is compared; a validator that rejects a
//     value the schema accepts is invisible.
//   - Operations are identified by path template plus method. A route parameter renamed in the
//     template (`/a/{id}` to `/a/{key}`) is reported as one removed and one added path.
//   - Validation keywords (length, range, pattern, uniqueItems, additionalProperties) are not compared.
//   - oneOf/anyOf variants are compared by position, and `not`, discriminator mappings, response
//     headers, links, callbacks and `x-` extensions are not compared at all.
//   - Security is compared per operation (the operation's own list, else the document's) as sets of
//     scheme names and scopes; what a scheme accepts is not described by the document.
import { execFileSync } from 'node:child_process'
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

const HTTP_METHODS = ['get', 'put', 'post', 'delete', 'options', 'head', 'patch', 'trace']
const MAX_REF_DEPTH = 32

// The registry of every finding this tool can produce. Emitting a kind that is not listed here
// throws, and the selftest requires that every listed kind is produced by at least one planted
// pair, so a class that can no longer fire cannot hide behind a green run.
const BREAKING = 'BREAKING'
const ADDITIVE = 'ADDITIVE'
export const KINDS = {
  'removed-path': BREAKING,
  'removed-operation': BREAKING,
  'removed-response-status': BREAKING,
  'removed-response-property': BREAKING,
  'retyped-response-property': BREAKING,
  'removed-enum-value': BREAKING,
  'response-property-became-nullable': BREAKING,
  'response-property-became-optional': BREAKING,
  'request-property-became-non-nullable': BREAKING,
  'removed-media-type': BREAKING,
  'new-required-request-parameter': BREAKING,
  'parameter-became-required': BREAKING,
  'parameter-location-changed': BREAKING,
  'parameter-removed': BREAKING,
  'retyped-request-parameter': BREAKING,
  'new-required-request-property': BREAKING,
  'request-property-became-required': BREAKING,
  'request-property-removed': BREAKING,
  'retyped-request-property': BREAKING,
  'new-required-request-body': BREAKING,
  'request-body-became-required': BREAKING,
  'request-body-removed': BREAKING,
  'security-requirement-added': BREAKING,
  'security-alternative-removed': BREAKING,
  'security-scope-added': BREAKING,
  'added-path': ADDITIVE,
  'added-operation': ADDITIVE,
  'added-response-status': ADDITIVE,
  'added-response-property': ADDITIVE,
  'added-enum-value': ADDITIVE,
  'added-media-type': ADDITIVE,
  'added-optional-request-parameter': ADDITIVE,
  'added-optional-request-property': ADDITIVE,
  'added-optional-request-body': ADDITIVE,
  'parameter-became-optional': ADDITIVE,
  'request-property-became-optional': ADDITIVE,
  'security-requirement-removed': ADDITIVE,
  'security-alternative-added': ADDITIVE,
}

const isObject = (value) => value !== null && typeof value === 'object' && !Array.isArray(value)

/** Throws a message the CLI reports as an unreadable input (status 2). */
class InputError extends Error {}

/** Validates the shape this tool relies on; a document that fails is refused, never guessed at. */
const assertDocument = (doc, label) => {
  if (!isObject(doc) || typeof doc.openapi !== 'string' || !doc.openapi.startsWith('3.')) {
    throw new InputError(`${label} is not an OpenAPI 3 document (no "openapi": "3.x" member)`)
  }
  if (!isObject(doc.paths)) throw new InputError(`${label} has no "paths" object`)
}

/** Follows `$ref` chains that point inside the same document. */
const resolve = (node, doc) => {
  let current = node
  for (let depth = 0; isObject(current) && typeof current.$ref === 'string'; depth++) {
    if (depth > MAX_REF_DEPTH) throw new InputError(`$ref chain longer than ${MAX_REF_DEPTH}: ${node.$ref}`)
    if (!current.$ref.startsWith('#/')) throw new InputError(`only local $ref is supported, got ${current.$ref}`)
    let target = doc
    for (const part of current.$ref.slice(2).split('/')) {
      target = target?.[part.replaceAll('~1', '/').replaceAll('~0', '~')]
    }
    if (target === undefined) throw new InputError(`dangling $ref ${current.$ref}`)
    current = target
  }
  return current
}

/** The schema and every `allOf` part beneath it, at any depth; a part met twice (or a cycle) counts once. */
const partsOf = (node, doc, seen) => {
  if (!isObject(node) || seen.has(node)) return []
  seen.add(node)
  const nested = Array.isArray(node.allOf) ? node.allOf.flatMap((part) => partsOf(resolve(part, doc), doc, seen)) : []
  return [node, ...nested]
}

/** Flattens a schema: resolves `$ref`, merges `allOf`, and reads the 3.0 and 3.1 spellings of null. */
const shapeOf = (schema, doc) => {
  const node = resolve(schema, doc)
  if (!isObject(node)) return { source: node, properties: {}, required: new Set() }
  const merged = { properties: {}, required: new Set(), enum: undefined, source: node }
  for (const part of partsOf(node, doc, new Set())) {
    Object.assign(merged.properties, part.properties ?? {})
    for (const name of part.required ?? []) merged.required.add(name)
    for (const key of ['type', 'format', 'items', 'additionalProperties', 'oneOf', 'anyOf']) {
      if (part[key] !== undefined && merged[key] === undefined) merged[key] = part[key]
    }
    if (part.enum !== undefined && merged.enum === undefined) merged.enum = part.enum
    if (part.nullable === true) merged.nullable = true
  }
  let types = Array.isArray(merged.type) ? merged.type : merged.type === undefined ? [] : [merged.type]
  if (types.includes('null')) {
    merged.nullable = true
    types = types.filter((entry) => entry !== 'null')
  }
  if (types.length === 0 && Object.keys(merged.properties).length > 0) types = ['object']
  if (types.length === 0 && merged.items !== undefined) types = ['array']
  merged.type = types.length === 0 ? undefined : [...types].sort().join('|')
  return merged
}

/** Collects findings; refuses a kind the registry does not name. */
const newContext = (oldDoc, newDoc) => ({
  oldDoc,
  newDoc,
  findings: [],
  seen: new Map(),
  add(kind, where) {
    if (KINDS[kind] === undefined) throw new Error(`unregistered finding kind: ${kind}`)
    this.findings.push({ severity: KINDS[kind], kind, where })
  },
})

/**
 * Compares two schemas in one direction. `direction` is 'request' or 'response'; `where` is the
 * human-readable location and grows as the walk descends.
 */
const compareSchema = (context, oldSchema, newSchema, direction, where) => {
  const before = shapeOf(oldSchema, context.oldDoc)
  const after = shapeOf(newSchema, context.newDoc)
  // The guard is an ANCESTOR stack, not a memo: a pair is skipped only while it is already being
  // compared higher up the same walk (a `$ref` cycle). A shared component used by many operations is
  // therefore compared, and reported, at every place it is used.
  const visited = context.seen.get(before.source) ?? new Map()
  context.seen.set(before.source, visited)
  const peers = visited.get(direction) ?? new Set()
  visited.set(direction, peers)
  if (peers.has(after.source)) return
  peers.add(after.source)
  try {
    compareShapes(context, before, after, direction, where)
  } finally {
    peers.delete(after.source)
  }
}

/** The body of {@link compareSchema}, once the cycle guard has let the pair through. */
const compareShapes = (context, before, after, direction, where) => {
  const retyped = direction === 'response' ? 'retyped-response-property' : 'retyped-request-property'
  if (before.type !== after.type || (before.format ?? null) !== (after.format ?? null)) {
    const was = `${before.type ?? 'any'}${before.format ? `:${before.format}` : ''}`
    const now = `${after.type ?? 'any'}${after.format ? `:${after.format}` : ''}`
    context.add(retyped, `${where} (${was} -> ${now})`)
    return
  }

  // Nullability is directional like everything else: a response that may now be null breaks a client
  // that read the value, a request that may no longer be null breaks a client that sent it.
  if (direction === 'response' && !before.nullable && after.nullable) context.add('response-property-became-nullable', where)
  if (direction === 'request' && before.nullable && !after.nullable) context.add('request-property-became-non-nullable', where)
  // A request that starts restricting its values to a list rejects what a client already sends.
  if (direction === 'request' && before.enum === undefined && after.enum !== undefined) context.add(retyped, `${where} (enum added)`)

  if (Array.isArray(before.enum) || Array.isArray(after.enum)) {
    const oldValues = new Set((before.enum ?? []).map((value) => JSON.stringify(value)))
    const newValues = new Set((after.enum ?? []).map((value) => JSON.stringify(value)))
    if (before.enum !== undefined) {
      for (const value of oldValues) {
        if (!newValues.has(value) && after.enum !== undefined) context.add('removed-enum-value', `${where} (${value})`)
      }
    }
    if (before.enum !== undefined && after.enum === undefined) context.add(retyped, `${where} (enum dropped)`)
    for (const value of newValues) {
      if (before.enum !== undefined && !oldValues.has(value)) context.add('added-enum-value', `${where} (${value})`)
    }
  }

  const names = new Set([...Object.keys(before.properties), ...Object.keys(after.properties)])
  for (const name of [...names].sort()) {
    const at = `${where}.${name}`
    const inBefore = name in before.properties
    const inAfter = name in after.properties
    if (inBefore && !inAfter) {
      context.add(direction === 'response' ? 'removed-response-property' : 'request-property-removed', at)
    } else if (!inBefore && inAfter) {
      if (direction === 'response') context.add('added-response-property', at)
      else context.add(after.required.has(name) ? 'new-required-request-property' : 'added-optional-request-property', at)
    } else {
      if (direction === 'response' && before.required.has(name) && !after.required.has(name)) {
        context.add('response-property-became-optional', at)
      }
      if (direction === 'request') {
        if (!before.required.has(name) && after.required.has(name)) context.add('request-property-became-required', at)
        if (before.required.has(name) && !after.required.has(name)) context.add('request-property-became-optional', at)
      }
      compareSchema(context, before.properties[name], after.properties[name], direction, at)
    }
  }

  if (isObject(before.items) && isObject(after.items)) {
    compareSchema(context, before.items, after.items, direction, `${where}[]`)
  }
  if (isObject(before.additionalProperties) && isObject(after.additionalProperties)) {
    compareSchema(context, before.additionalProperties, after.additionalProperties, direction, `${where}{}`)
  }
  for (const combinator of ['oneOf', 'anyOf']) {
    const left = before[combinator]
    const right = after[combinator]
    if (!Array.isArray(left) || !Array.isArray(right)) continue
    for (let index = 0; index < Math.min(left.length, right.length); index++) {
      compareSchema(context, left[index], right[index], direction, `${where}<${combinator}${index}>`)
    }
  }
}

/** Compares the `content` maps of two bodies (a response or a request body). */
const compareContent = (context, oldBody, newBody, direction, where) => {
  const left = oldBody?.content ?? {}
  const right = newBody?.content ?? {}
  for (const media of [...new Set([...Object.keys(left), ...Object.keys(right)])].sort()) {
    const at = `${where} ${media}`
    if (!(media in right)) context.add('removed-media-type', at)
    else if (!(media in left)) context.add('added-media-type', at)
    else if (left[media].schema !== undefined && right[media].schema !== undefined) {
      compareSchema(context, left[media].schema, right[media].schema, direction, `${at} $`)
    }
  }
}

/** The parameters of an operation, path-level first and overridden by the operation's own. */
const parametersOf = (pathItem, operation, doc) => {
  const byKey = new Map()
  for (const raw of [...(pathItem.parameters ?? []), ...(operation.parameters ?? [])]) {
    const parameter = resolve(raw, doc)
    byKey.set(`${parameter.in}:${parameter.name}`, parameter)
  }
  return byKey
}

const compareParameters = (context, oldPath, oldOp, newPath, newOp, where) => {
  const before = parametersOf(oldPath, oldOp, context.oldDoc)
  const after = parametersOf(newPath, newOp, context.newDoc)
  const handled = new Set()
  for (const [key, parameter] of [...before].sort(([a], [b]) => a.localeCompare(b))) {
    const at = `${where} parameter ${parameter.name}`
    const peer = after.get(key)
    if (peer === undefined) {
      const moved = [...after].find(([otherKey, other]) => other.name === parameter.name && !before.has(otherKey))
      if (moved === undefined) context.add('parameter-removed', at)
      else {
        handled.add(moved[0])
        context.add('parameter-location-changed', `${at} (${parameter.in} -> ${moved[1].in})`)
      }
      continue
    }
    if (!parameter.required && peer.required) context.add('parameter-became-required', at)
    if (parameter.required && !peer.required) context.add('parameter-became-optional', at)
    if (parameter.schema !== undefined && peer.schema !== undefined) {
      const sub = newContext(context.oldDoc, context.newDoc)
      sub.seen = context.seen
      compareSchema(sub, parameter.schema, peer.schema, 'request', at)
      for (const finding of sub.findings) {
        context.add(finding.kind === 'retyped-request-property' ? 'retyped-request-parameter' : finding.kind, finding.where)
      }
    }
  }
  for (const [key, parameter] of [...after].sort(([a], [b]) => a.localeCompare(b))) {
    if (before.has(key) || handled.has(key)) continue
    context.add(parameter.required ? 'new-required-request-parameter' : 'added-optional-request-parameter',
      `${where} parameter ${parameter.name}`)
  }
}

const compareRequestBody = (context, oldOp, newOp, where) => {
  const before = oldOp.requestBody === undefined ? undefined : resolve(oldOp.requestBody, context.oldDoc)
  const after = newOp.requestBody === undefined ? undefined : resolve(newOp.requestBody, context.newDoc)
  const at = `${where} request`
  if (before === undefined && after === undefined) return
  if (before === undefined) {
    context.add(after.required ? 'new-required-request-body' : 'added-optional-request-body', at)
    return
  }
  if (after === undefined) {
    context.add('request-body-removed', at)
    return
  }
  if (!before.required && after.required) context.add('request-body-became-required', at)
  compareContent(context, before, after, 'request', at)
}

const compareResponses = (context, oldOp, newOp, where) => {
  const before = oldOp.responses ?? {}
  const after = newOp.responses ?? {}
  for (const status of [...new Set([...Object.keys(before), ...Object.keys(after)])].sort()) {
    const at = `${where} ${status}`
    if (!(status in after)) context.add('removed-response-status', at)
    else if (!(status in before)) context.add('added-response-status', at)
    else {
      compareContent(context, resolve(before[status], context.oldDoc), resolve(after[status], context.newDoc), 'response', at)
    }
  }
}

/** The operation's effective alternatives: its own list when it declares one, else the document's. */
const alternativesOf = (operation, doc) => {
  const list = operation.security !== undefined ? operation.security : doc.security ?? []
  const byKey = new Map()
  for (const requirement of list) {
    const schemes = Object.keys(requirement).sort()
    const scopes = new Set(schemes.flatMap((scheme) => requirement[scheme] ?? []))
    byKey.set(schemes.join('+'), scopes)
  }
  return byKey
}

const compareSecurity = (context, oldOp, newOp, where) => {
  const before = alternativesOf(oldOp, context.oldDoc)
  const after = alternativesOf(newOp, context.newDoc)
  const open = (alternatives) => alternatives.size === 0 || alternatives.has('')
  const at = `${where} security`
  if (open(before) && !open(after)) return context.add('security-requirement-added', at)
  if (!open(before) && open(after)) return context.add('security-requirement-removed', at)
  if (open(before)) return undefined
  for (const [key, scopes] of before) {
    if (!after.has(key)) context.add('security-alternative-removed', `${at} (${key})`)
    else for (const scope of after.get(key)) {
      if (!scopes.has(scope)) context.add('security-scope-added', `${at} (${key}: ${scope})`)
    }
  }
  for (const key of after.keys()) if (!before.has(key)) context.add('security-alternative-added', `${at} (${key})`)
  return undefined
}

/**
 * Diffs two OpenAPI 3 documents.
 * @returns {{findings: {severity: string, kind: string, where: string}[], operations: {before: number, after: number}}}
 */
export const diffDocuments = (oldDoc, newDoc) => {
  assertDocument(oldDoc, 'the baseline')
  assertDocument(newDoc, 'the generated document')
  const context = newContext(oldDoc, newDoc)
  const operations = { before: 0, after: 0 }
  const methodsOf = (item) => HTTP_METHODS.filter((method) => isObject(item[method]))
  for (const item of Object.values(oldDoc.paths)) operations.before += methodsOf(item).length
  for (const item of Object.values(newDoc.paths)) operations.after += methodsOf(item).length

  for (const path of [...new Set([...Object.keys(oldDoc.paths), ...Object.keys(newDoc.paths)])].sort()) {
    const before = oldDoc.paths[path]
    const after = newDoc.paths[path]
    if (after === undefined) context.add('removed-path', path)
    else if (before === undefined) context.add('added-path', path)
    else {
      for (const method of HTTP_METHODS) {
        const where = `${method.toUpperCase()} ${path}`
        if (isObject(before[method]) && !isObject(after[method])) context.add('removed-operation', where)
        else if (!isObject(before[method]) && isObject(after[method])) context.add('added-operation', where)
        else if (isObject(before[method])) {
          compareParameters(context, before, before[method], after, after[method], where)
          compareRequestBody(context, before[method], after[method], where)
          compareResponses(context, before[method], after[method], where)
          compareSecurity(context, before[method], after[method], where)
        }
      }
    }
  }

  const rank = { [BREAKING]: 0, [ADDITIVE]: 1 }
  const unique = new Map(context.findings.map((f) => [`${f.severity} ${f.kind} ${f.where}`, f]))
  const findings = [...unique.values()].sort(
    (a, b) => rank[a.severity] - rank[b.severity] || `${a.kind} ${a.where}`.localeCompare(`${b.kind} ${b.where}`),
  )
  return { findings, operations }
}

/** Deep copy with object keys sorted and `required` lists sorted, so regenerating is byte-stable. */
const canonical = (value, key) => {
  if (Array.isArray(value)) {
    const copy = value.map((entry) => canonical(entry))
    return key === 'required' ? copy.sort() : copy
  }
  if (!isObject(value)) return value
  return Object.fromEntries(Object.keys(value).sort().map((name) => [name, canonical(value[name], name)]))
}

const render = (doc) => `${JSON.stringify(canonical(doc), null, 2)}\n`

const readDocument = (path, label) => {
  try {
    return JSON.parse(readFileSync(path, 'utf8'))
  } catch (error) {
    throw new InputError(`${label} (${path}) is not readable JSON: ${error.message}`)
  }
}

/** Prints the findings and the one summary line; returns the process status. */
const report = (oldDoc, newDoc) => {
  const { findings, operations } = diffDocuments(oldDoc, newDoc)
  for (const finding of findings) console.log(`${finding.severity.padEnd(8)} ${finding.kind}  ${finding.where}`)
  const breaking = findings.filter((finding) => finding.severity === BREAKING).length
  console.log(
    `openapi-diff: ${operations.before} baseline operations, ${operations.after} generated operations; ` +
      `${breaking} breaking, ${findings.length - breaking} additive.`,
  )
  if (operations.before === 0 || operations.after === 0) {
    console.log('openapi-diff: VACUOUS — a document describes no operation, so nothing was compared. That is a FAILURE, not a pass.')
    return 1
  }
  return breaking > 0 ? 1 : 0
}

// ---------------------------------------------------------------------------------------------
// SELFTEST. Every registered kind is planted at least once, each planted pair must produce EXACTLY
// its expected kinds (so a detector that over-reports fails too), and the inverse controls hand the
// same walk documents it must accept. The coverage assertion at the end reads the registry, so a new
// kind with no planted pair fails here rather than shipping untested.
// ---------------------------------------------------------------------------------------------

const baseDocument = () => ({
  openapi: '3.0.1',
  info: { title: 'selftest', version: '1' },
  paths: {
    '/things': {
      get: {
        parameters: [{ name: 'page', in: 'query', required: false, schema: { type: 'integer', format: 'int32' } }],
        responses: {
          200: {
            description: 'ok',
            content: {
              'application/json': {
                schema: {
                  type: 'object',
                  required: ['id'],
                  properties: {
                    id: { type: 'string', format: 'uuid' },
                    status: { type: 'string', enum: ['open', 'closed'] },
                    owner: { $ref: '#/components/schemas/Owner' },
                    tags: { type: 'array', items: { type: 'string' } },
                  },
                },
              },
            },
          },
          404: { description: 'missing' },
        },
        security: [{ Bearer: [] }],
      },
      post: {
        requestBody: {
          required: true,
          content: {
            'application/json': {
              schema: {
                type: 'object',
                required: ['name'],
                properties: { name: { type: 'string' }, note: { type: 'string' }, kind: { type: 'string', enum: ['a', 'b'] } },
              },
            },
          },
        },
        responses: { 201: { description: 'created' } },
        security: [{ Bearer: [] }],
      },
    },
    '/open': { get: { responses: { 200: { description: 'public' } } } },
    '/gone': { get: { responses: { 200: { description: 'will be removed' } } } },
  },
  components: {
    schemas: {
      Owner: {
        type: 'object',
        properties: { name: { type: 'string' }, parent: { $ref: '#/components/schemas/Owner' } },
      },
    },
  },
})

const clone = (value) => JSON.parse(JSON.stringify(value))
const thingsGet = (doc) => doc.paths['/things'].get
const thingsPost = (doc) => doc.paths['/things'].post
const responseSchema = (doc) => thingsGet(doc).responses[200].content['application/json'].schema
const requestSchema = (doc) => thingsPost(doc).requestBody.content['application/json'].schema

// Each case: the kind it plants, a mutation applied to a copy of the base document, an optional note,
// and the exact list of kinds the diff must report (the planted kind alone unless a mutation
// inherently implies more). Cases that need a different base document are in specialPairs().
const PLANTED = [
  ['removed-path', (d) => { delete d.paths['/gone'] }],
  ['removed-operation', (d) => { delete d.paths['/things'].post }],
  ['removed-response-status', (d) => { delete thingsGet(d).responses[404] }],
  ['removed-response-property', (d) => { delete responseSchema(d).properties.tags }],
  ['removed-response-property', (d) => { delete d.components.schemas.Owner.properties.name }, 'a property removed inside a $ref target'],
  ['retyped-response-property', (d) => { responseSchema(d).properties.id.type = 'integer' }],
  ['retyped-response-property', (d) => { responseSchema(d).properties.id.format = 'int64' }, 'a format-only change'],
  ['retyped-response-property', (d) => { responseSchema(d).properties.tags.items.type = 'integer' }, 'a retyped array item'],
  ['removed-enum-value', (d) => { responseSchema(d).properties.status.enum = ['open'] }],
  ['response-property-became-nullable', (d) => { responseSchema(d).properties.id.nullable = true }],
  ['response-property-became-optional', (d) => { responseSchema(d).required = [] }],
  ['retyped-request-property', (d) => { requestSchema(d).properties.name.enum = ['x'] }, 'a request property that gained an enum'],
  ['new-required-request-parameter', (d) => { thingsGet(d).parameters.push({ name: 'tenant', in: 'header', required: true, schema: { type: 'string' } }) }],
  ['parameter-became-required', (d) => { thingsGet(d).parameters[0].required = true }],
  ['parameter-location-changed', (d) => { thingsGet(d).parameters[0].in = 'header' }],
  ['parameter-removed', (d) => { thingsGet(d).parameters.length = 0 }],
  ['retyped-request-parameter', (d) => { thingsGet(d).parameters[0].schema.type = 'string' }],
  ['new-required-request-property', (d) => { const s = requestSchema(d); s.properties.owner = { type: 'string' }; s.required.push('owner') }],
  ['request-property-became-required', (d) => { requestSchema(d).required.push('note') }],
  ['request-property-removed', (d) => { delete requestSchema(d).properties.note }],
  ['retyped-request-property', (d) => { requestSchema(d).properties.name.type = 'integer' }],
  ['removed-enum-value', (d) => { requestSchema(d).properties.kind.enum = ['a'] }, 'a request enum narrowed'],
  ['new-required-request-body', (d) => { d.paths['/open'].get.requestBody = { required: true, content: { 'application/json': { schema: { type: 'object' } } } } }],
  ['request-body-removed', (d) => { delete thingsPost(d).requestBody }],
  ['security-requirement-added', (d) => { d.paths['/open'].get.security = [{ Bearer: [] }] }],
  ['security-alternative-removed', (d) => { thingsGet(d).security = [{ ApiKey: [] }] }, 'Bearer replaced by ApiKey', ['security-alternative-removed', 'security-alternative-added']],
  ['security-scope-added', (d) => { thingsGet(d).security = [{ Bearer: ['server.read'] }] }],
  ['added-path', (d) => { d.paths['/fresh'] = { get: { responses: { 200: { description: 'new' } } } } }],
  ['added-operation', (d) => { d.paths['/open'].post = { responses: { 200: { description: 'new' } } } }],
  ['added-response-status', (d) => { thingsGet(d).responses[409] = { description: 'new' } }],
  ['added-response-property', (d) => { responseSchema(d).properties.extra = { type: 'string' } }],
  ['added-enum-value', (d) => { responseSchema(d).properties.status.enum.push('archived') }],
  ['added-media-type', (d) => { thingsGet(d).responses[200].content['text/csv'] = { schema: { type: 'string' } } }],
  ['added-optional-request-parameter', (d) => { thingsGet(d).parameters.push({ name: 'sort', in: 'query', required: false, schema: { type: 'string' } }) }],
  ['added-optional-request-property', (d) => { requestSchema(d).properties.extra = { type: 'string' } }],
  ['added-optional-request-body', (d) => { d.paths['/open'].get.requestBody = { content: { 'application/json': { schema: { type: 'object' } } } } }],
  ['request-property-became-optional', (d) => { requestSchema(d).required = [] }],
  ['security-requirement-removed', (d) => { delete thingsGet(d).security; delete thingsPost(d).security }, 'both operations public', ['security-requirement-removed', 'security-requirement-removed']],
  ['security-alternative-added', (d) => { thingsGet(d).security.push({ ApiKey: [] }) }],
]

/** The planted pairs that need a base document other than the shared one. */
const specialPairs = () => {
  const pairs = []
  const withContent = (d) => { thingsPost(d).requestBody.content['text/plain'] = { schema: { type: 'string' } } }
  const base = baseDocument(); withContent(base)
  const mutated = baseDocument()
  pairs.push(['removed-media-type', 'a request media type removed', base, mutated, ['removed-media-type']])

  const optionalBody = baseDocument(); thingsPost(optionalBody).requestBody.required = false
  pairs.push(['request-body-became-required', 'optional body made required', optionalBody, baseDocument(), ['request-body-became-required']])

  const required = baseDocument(); thingsGet(required).parameters[0].required = true
  pairs.push(['parameter-became-optional', 'required query parameter made optional', required, baseDocument(), ['parameter-became-optional']])

  const nullableName = baseDocument(); requestSchema(nullableName).properties.name.nullable = true
  pairs.push(['request-property-became-non-nullable', 'a nullable request property made non-null', nullableName, baseDocument(), ['request-property-became-non-nullable']])

  const nest = (leafProperties) => {
    const d = baseDocument()
    d.components.schemas.Leaf = { type: 'object', properties: leafProperties }
    d.components.schemas.Mid = { allOf: [{ $ref: '#/components/schemas/Leaf' }] }
    responseSchema(d).properties.nested = { allOf: [{ $ref: '#/components/schemas/Mid' }] }
    return d
  }
  pairs.push(['removed-response-property', 'a property removed two allOf levels down', nest({ x: { type: 'string' } }), nest({}), ['removed-response-property']])
  return pairs
}

const kindsOf = (oldDoc, newDoc) => diffDocuments(oldDoc, newDoc).findings.map((finding) => finding.kind).sort()

const runSelfTest = () => {
  const problems = []
  const produced = new Set()
  const expectKinds = (label, oldDoc, newDoc, expected) => {
    let actual
    try {
      actual = kindsOf(oldDoc, newDoc)
    } catch (error) {
      problems.push(`${label}: threw ${error.message}`)
      return
    }
    for (const kind of actual) produced.add(kind)
    if (JSON.stringify(actual) !== JSON.stringify([...expected].sort())) {
      problems.push(`${label}: expected [${[...expected].sort()}] but got [${actual}]`)
    }
  }

  for (const entry of PLANTED) {
    const [kind, mutate, note, expectedList] = entry
    const mutated = baseDocument()
    mutate(mutated)
    expectKinds(`planted ${kind}${note ? ` (${note})` : ''}`, baseDocument(), mutated, expectedList ?? [kind])
  }
  // The response-side media-type cases, both directions of the same pair.
  {
    const mutated = baseDocument()
    thingsGet(mutated).responses[200].content['text/plain'] = {}
    expectKinds('planted added-media-type (response)', baseDocument(), mutated, ['added-media-type'])
    expectKinds('planted removed-media-type (response)', mutated, baseDocument(), ['removed-media-type'])
  }
  for (const [kind, note, oldDoc, newDoc, expected] of specialPairs()) {
    expectKinds(`planted ${kind} (${note})`, oldDoc, newDoc, expected)
  }

  // INVERSE CONTROLS. A detector mutated to flag everything passes every test that only hands it a
  // broken pair, so these hand it pairs it must accept.
  expectKinds('control: identical documents', baseDocument(), baseDocument(), [])
  {
    const reordered = clone(baseDocument())
    reordered.paths = Object.fromEntries(Object.entries(reordered.paths).reverse())
    responseSchema(reordered).required = ['id']
    thingsGet(reordered).responses[200].description = 'a reworded description'
    reordered.info.version = '2'
    reordered.paths['/things'].get.summary = 'added prose'
    expectKinds('control: key order, descriptions and version bump are not changes', baseDocument(), reordered, [])
  }
  {
    const widened = baseDocument()
    widened.paths['/fresh'] = { get: { responses: { 200: { description: 'new' } } } }
    responseSchema(widened).properties.extra = { type: 'string' }
    requestSchema(widened).properties.extra = { type: 'string' }
    thingsGet(widened).responses[409] = { description: 'new' }
    const result = diffDocuments(baseDocument(), widened)
    if (result.findings.some((finding) => finding.severity === BREAKING)) {
      problems.push('control: a purely additive change was reported BREAKING')
    }
    if (result.findings.length !== 4) problems.push(`control: additive change should give 4 findings, got ${result.findings.length}`)
  }
  {
    // Directions that are NOT breaking: a response that stops being nullable, a request that starts
    // accepting null, and a response that gains an enum.
    const nullableResponse = baseDocument(); responseSchema(nullableResponse).properties.id.nullable = true
    expectKinds('control: a response that stops being nullable is not a change a client feels', nullableResponse, baseDocument(), [])
    const nullableRequest = baseDocument(); requestSchema(nullableRequest).properties.name.nullable = true
    expectKinds('control: a request that starts accepting null is not breaking', baseDocument(), nullableRequest, [])
    const enumResponse = baseDocument(); responseSchema(enumResponse).properties.id.enum = ['x']
    expectKinds('control: a response that gains an enum is not breaking', baseDocument(), enumResponse, [])
  }
  {
    // A $ref cycle (Owner.parent -> Owner) must terminate and still see a change inside the cycle.
    const mutated = baseDocument()
    mutated.components.schemas.Owner.properties.name.type = 'integer'
    expectKinds('control: a cycle terminates and still reports a change inside it', baseDocument(), mutated, ['retyped-response-property'])
  }
  {
    // Vacuity: an empty comparison is refused by the reporter, not passed.
    const empty = { openapi: '3.0.1', info: { title: 'e', version: '1' }, paths: {} }
    const quiet = console.log
    console.log = () => {}
    let status
    try { status = report(empty, empty) } finally { console.log = quiet }
    if (status !== 1) problems.push('control: two documents with no operations must be a failure (vacuous), not a pass')
  }
  for (const [label, bad] of [['no openapi member', { paths: {} }], ['no paths', { openapi: '3.0.1' }], ['openapi 2', { swagger: '2.0', paths: {} }]]) {
    let refused = false
    try { diffDocuments(bad, baseDocument()) } catch (error) { refused = error instanceof InputError }
    if (!refused) problems.push(`control: a document with ${label} must be refused as input`)
  }

  // Coverage: every registered kind was produced by at least one planted pair.
  for (const kind of Object.keys(KINDS)) {
    if (!produced.has(kind)) problems.push(`coverage: kind '${kind}' is registered but no planted pair produced it`)
  }
  for (const kind of produced) {
    if (KINDS[kind] === undefined) problems.push(`coverage: kind '${kind}' was produced but is not registered`)
  }
  // The registry itself must hold both classes, or the per-class claims above are vacuous.
  if (!Object.values(KINDS).includes(BREAKING) || !Object.values(KINDS).includes(ADDITIVE)) {
    problems.push('coverage: the registry holds only one severity class')
  }

  // The CLI round trip: exit status and canonical output through real files and a real child process.
  const directory = mkdtempSync(join(tmpdir(), 'openapi-diff-selftest-'))
  try {
    const script = fileURLToPath(import.meta.url)
    const oldPath = join(directory, 'old.json')
    const newPath = join(directory, 'new.json')
    const breakingDoc = baseDocument()
    delete breakingDoc.paths['/gone']
    writeFileSync(oldPath, render(baseDocument()))
    const run = (a, b) => {
      try {
        execFileSync(process.execPath, [script, 'diff', a, b], { stdio: 'pipe' })
        return 0
      } catch (error) {
        return error.status
      }
    }
    writeFileSync(newPath, render(baseDocument()))
    if (run(oldPath, newPath) !== 0) problems.push('cli: identical documents must exit 0')
    writeFileSync(newPath, render(breakingDoc))
    if (run(oldPath, newPath) !== 1) problems.push('cli: a breaking pair must exit 1')
    writeFileSync(newPath, '{not json')
    if (run(oldPath, newPath) !== 2) problems.push('cli: an unreadable document must exit 2')
    const canonPath = join(directory, 'canon.json')
    execFileSync(process.execPath, [script, 'canon', oldPath, canonPath], { stdio: 'pipe' })
    if (readFileSync(canonPath, 'utf8') !== readFileSync(oldPath, 'utf8')) problems.push('cli: canonical form must be stable')
  } finally {
    rmSync(directory, { recursive: true, force: true })
  }

  if (problems.length > 0) {
    console.error('openapi-diff selftest FAILED:')
    for (const problem of problems) console.error(`  ${problem}`)
    return 1
  }
  console.log(
    `openapi-diff selftest: ${Object.keys(KINDS).length} finding kinds each produced by a planted pair ` +
      `(${Object.values(KINDS).filter((s) => s === BREAKING).length} breaking, ` +
      `${Object.values(KINDS).filter((s) => s === ADDITIVE).length} additive), inverse controls accepted.`,
  )
  return 0
}

const main = () => {
  const [verb, first, second] = process.argv.slice(2)
  try {
    if (verb === 'selftest') return runSelfTest()
    if (verb === 'diff' && first && second) return report(readDocument(first, 'the baseline'), readDocument(second, 'the generated document'))
    if (verb === 'canon' && first && second) {
      writeFileSync(second, render(readDocument(first, 'the document')))
      return 0
    }
  } catch (error) {
    if (!(error instanceof InputError)) throw error
    console.error(`openapi-diff: ${error.message}`)
    return 2
  }
  console.error('usage: openapi-diff.mjs diff <baseline.json> <generated.json> | canon <in> <out> | selftest')
  return 2
}

if (process.argv[1] === fileURLToPath(import.meta.url)) process.exit(main())
