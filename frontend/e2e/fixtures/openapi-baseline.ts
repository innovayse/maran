import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

/** One OpenAPI 3.0 schema object, limited to the keywords the committed baseline uses. */
export interface OpenApiSchema {
  /** A reference such as `#/components/schemas/Modules.Accounts.Common.PlanDto`. */
  $ref?: string
  /** The JSON type. */
  type?: string
  /** Whether `null` is an accepted value. */
  nullable?: boolean
  /** A format hint such as `uuid` or `date-time`. */
  format?: string
  /** The values an enumeration accepts. */
  enum?: readonly unknown[]
  /** The named fields of an object. */
  properties?: Record<string, OpenApiSchema>
  /** The fields an object must carry. */
  required?: readonly string[]
  /** `false` forbids unknown fields; a schema types them. */
  additionalProperties?: boolean | OpenApiSchema
  /** The element schema of an array. */
  items?: OpenApiSchema
  /** Every listed schema must accept the value. */
  allOf?: readonly OpenApiSchema[]
  /** Exactly one listed schema should accept the value. */
  oneOf?: readonly OpenApiSchema[]
  /** At least one listed schema must accept the value. */
  anyOf?: readonly OpenApiSchema[]
}

/** One documented response of an operation. */
interface OpenApiResponse {
  /** The bodies by media type. */
  content?: Record<string, { schema?: OpenApiSchema }>
}

/** One HTTP operation of the baseline. */
interface OpenApiOperation {
  /** The documented responses by status code. */
  responses?: Record<string, OpenApiResponse>
}

/** The parts of the committed OpenAPI document the contract check reads. */
export interface OpenApiDocument {
  /** The operations by path template, then by lower-case method. */
  paths: Record<string, Record<string, OpenApiOperation>>
  /** The named schemas `$ref` points into. */
  components: { schemas: Record<string, OpenApiSchema> }
}

/** What the baseline says about one status of one operation. */
export type ResponseExpectation =
  | { kind: 'undocumented' }
  | { kind: 'no-body' }
  | { kind: 'schema'; schema: OpenApiSchema }

/** A path template of the baseline with the expression that recognises its concrete paths. */
interface PathTemplate {
  /** The template as documented, `{id}` placeholders included. */
  template: string
  /** Matches a concrete request path. */
  pattern: RegExp
  /** How many segments are literal; the template with more of them is the more specific one. */
  literalSegments: number
}

/** The committed baseline, relative to this file. */
const BASELINE_FILE = resolve(
  dirname(fileURLToPath(import.meta.url)),
  '..',
  '..',
  '..',
  'docs',
  'api',
  'openapi-baseline.json',
)

/** The parsed baseline, read once per worker. */
let document: OpenApiDocument | null = null

/** The path templates of {@link document}, built when it is first read. */
let templates: PathTemplate[] = []

/**
 * Escapes the characters of a literal path segment that a regular expression would read as syntax.
 * @param segment One literal path segment.
 * @returns The segment, safe to place inside a pattern.
 */
const escapeSegment = (segment: string): string => {
  return segment.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

/**
 * Reads `docs/api/openapi-baseline.json` once and indexes its path templates.
 * @returns The parsed document.
 */
export const readBaseline = (): OpenApiDocument => {
  if (document !== null) {
    return document
  }
  document = JSON.parse(readFileSync(BASELINE_FILE, 'utf8')) as OpenApiDocument
  templates = Object.keys(document.paths).map((template): PathTemplate => {
    const segments = template.split('/')
    const pattern = segments
      .map((segment) => {
        return segment.startsWith('{') ? '[^/]+' : escapeSegment(segment)
      })
      .join('/')
    return {
      template,
      pattern: new RegExp(`^${pattern}$`),
      literalSegments: segments.filter((segment) => {return !segment.startsWith('{')}).length,
    }
  })
  return document
}

/**
 * Finds the operation a concrete request belongs to.
 * @param method The HTTP method, in any case.
 * @param pathname The request path without its query string.
 * @returns The documented template and operation, or `null` when the baseline has no such operation.
 */
export const findOperation = (
  method: string,
  pathname: string,
): { template: string; operation: OpenApiOperation } | null => {
  const baseline = readBaseline()
  const candidates = templates
    .filter((entry) => {return entry.pattern.test(pathname) && baseline.paths[entry.template]?.[method.toLowerCase()]})
    // `/api/v1/sites/php-versions` is both a literal path and an instance of `/api/v1/sites/{id}`;
    // the one with more literal segments is the one the router picks.
    .sort((left, right) => {return right.literalSegments - left.literalSegments})
  const best = candidates[0]
  const operation = best ? baseline.paths[best.template]?.[method.toLowerCase()] : undefined
  return best && operation ? { template: best.template, operation } : null
}

/**
 * Reads what an operation documents for one status.
 * @param operation The operation, from {@link findOperation}.
 * @param status The HTTP status the response carried.
 * @returns The expectation: undocumented, documented without a body, or documented with a schema.
 */
export const expectationFor = (operation: OpenApiOperation, status: number): ResponseExpectation => {
  const response = operation.responses?.[String(status)]
  if (!response) {
    return { kind: 'undocumented' }
  }
  const content = response.content
  const media = content?.['application/json'] ?? content?.['application/problem+json']
  return media?.schema ? { kind: 'schema', schema: media.schema } : { kind: 'no-body' }
}

/**
 * Looks a named schema up by the reference that points at it.
 * @param reference A `#/components/schemas/<name>` reference.
 * @returns The schema, or `null` when the baseline has no such name.
 */
export const resolveReference = (reference: string): OpenApiSchema | null => {
  const prefix = '#/components/schemas/'
  if (!reference.startsWith(prefix)) {
    return null
  }
  return readBaseline().components.schemas[reference.slice(prefix.length)] ?? null
}

/**
 * Looks a named schema up by its name, for a check that validates a fixture directly.
 * @param name The schema's name, e.g. `Modules.Accounts.Common.PlanDto`.
 * @returns The schema, or `null` when the baseline has no such name.
 */
export const schemaNamed = (name: string): OpenApiSchema | null => {
  return resolveReference(`#/components/schemas/${name}`)
}
