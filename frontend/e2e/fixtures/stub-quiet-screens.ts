import type { Page, Route } from '@playwright/test'
import { expectationFor, findOperation, resolveReference, type OpenApiSchema } from './openapi-baseline'

/** The all-zero identity every sampled `uuid` field carries. */
const SAMPLE_UUID = '00000000-0000-0000-0000-000000000000'

/** The instant every sampled `date-time` field carries. */
const SAMPLE_INSTANT = '2026-01-01T00:00:00+00:00'

/** How deep a sampled object nests before it is cut to `{}`; a recursive contract must end. */
const MAX_DEPTH = 6

/** The page size a sampled list claims, so a pager drawn from it is not dividing by zero. */
const SAMPLE_PAGE_SIZE = 25

/**
 * Builds the quietest value a schema accepts: nothing in a list, nothing set where the contract allows
 * null, zero, an empty sentence, the first member of an enumeration.
 * @param schema The schema, possibly a `$ref`.
 * @param name The property the value is for, which tells a list's `page` and `pageSize` from other numbers.
 * @param depth How deep the value already nests.
 * @returns A JSON value that conforms to the schema.
 */
const quietValueOf = (schema: OpenApiSchema, name: string, depth: number): unknown => {
  if (schema.$ref !== undefined) {
    const target = resolveReference(schema.$ref)
    return target === null ? null : quietValueOf(target, name, depth)
  }
  if (schema.nullable === true) {
    return null
  }
  if (schema.enum !== undefined && schema.enum.length > 0) {
    return schema.enum[0]
  }
  if (schema.allOf !== undefined && schema.allOf.length > 0) {
    const merged: Record<string, unknown> = {}
    for (const part of schema.allOf) {
      const value = quietValueOf(part, name, depth)
      if (typeof value === 'object' && value !== null && !Array.isArray(value)) {
        Object.assign(merged, value)
      }
    }
    return merged
  }
  const alternative = [...(schema.oneOf ?? []), ...(schema.anyOf ?? [])][0]
  if (alternative !== undefined) {
    return quietValueOf(alternative, name, depth)
  }
  switch (schema.type) {
    case 'array':
      return []
    case 'string':
      if (schema.format === 'uuid') {
        return SAMPLE_UUID
      }
      return schema.format === 'date-time' ? SAMPLE_INSTANT : ''
    case 'integer':
    case 'number':
      if (name === 'page') {
        return 1
      }
      return name === 'pageSize' ? SAMPLE_PAGE_SIZE : 0
    case 'boolean':
      return false
    default: {
      if (depth >= MAX_DEPTH) {
        return {}
      }
      const sampled: Record<string, unknown> = {}
      for (const [key, field] of Object.entries(schema.properties ?? {})) {
        sampled[key] = quietValueOf(field, key, depth + 1)
      }
      return sampled
    }
  }
}

/**
 * Answers every `GET` of the panel's documented API with the quietest body its contract allows, so
 * a spec that clicks through screens it is not about does not leave their reads unanswered. The
 * answer is built from `docs/api/openapi-baseline.json`, so it conforms by construction and cannot
 * drift from it the way a hand-written body can.
 *
 * Only what the baseline documents with a JSON body is answered. A write, a stream, a download and a
 * request the baseline does not know fall through (`fallback`) to the guard's catch-all, which counts
 * them: the unanswered-request check stays able to see everything this does not cover.
 *
 * Install it FIRST: Playwright consults routes newest-first, so every narrower stub the spec adds
 * afterwards wins, and this only answers what nothing else did.
 * @param page The Playwright page whose network the route is installed on.
 * @returns Resolves once the route is installed.
 */
export const stubQuietScreens = async (page: Page): Promise<void> => {
  await page.route(/\/api\/v1\//, async (route: Route) => {
    const request = route.request()
    const found = request.method() === 'GET' ? findOperation('GET', new URL(request.url()).pathname) : null
    const expectation = found === null ? null : expectationFor(found.operation, 200)
    if (expectation === null || expectation.kind !== 'schema') {
      await route.fallback()
      return
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(quietValueOf(expectation.schema, '', 0)),
    })
  })
}
