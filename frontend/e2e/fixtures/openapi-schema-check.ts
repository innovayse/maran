import { resolveReference, type OpenApiSchema } from './openapi-baseline'

/** Most findings reported for one body; a drifted shape repeats itself and the first few say it all. */
const MAX_FINDINGS = 10

/** The textual shape of a canonical UUID. */
const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/**
 * Names the JSON type of a value in the vocabulary of OpenAPI.
 * @param value Any parsed JSON value.
 * @returns `null`, `array`, `integer`, `number`, `string`, `boolean` or `object`.
 */
const typeOf = (value: unknown): string => {
  if (value === null) {
    return 'null'
  }
  if (Array.isArray(value)) {
    return 'array'
  }
  if (typeof value === 'number') {
    return Number.isInteger(value) ? 'integer' : 'number'
  }
  return typeof value
}

/**
 * Whether a value is acceptable for a declared type (an integer is also a number).
 * @param declared The schema's `type`.
 * @param actual The value's type from {@link typeOf}.
 * @returns True when the value may stand where the schema puts it.
 */
const typeAccepts = (declared: string, actual: string): boolean => {
  return declared === actual || (declared === 'number' && actual === 'integer')
}

/**
 * Checks one value against one schema and returns what is wrong with it.
 * @param schema The schema, possibly a `$ref`.
 * @param value The parsed JSON value.
 * @param path Where the value sits in the body, for the message.
 * @returns One sentence per disagreement; empty when the value conforms.
 */
export const checkAgainstSchema = (schema: OpenApiSchema, value: unknown, path: string): string[] => {
  if (schema.$ref !== undefined) {
    const target = resolveReference(schema.$ref)
    if (target === null) {
      return [`${path}: the contract points at ${schema.$ref}, which it does not define`]
    }
    return checkAgainstSchema(target, value, path)
  }
  if (value === null) {
    return schema.nullable === true ? [] : [`${path}: is null where the contract does not allow null`]
  }

  const findings: string[] = []

  for (const part of schema.allOf ?? []) {
    findings.push(...checkAgainstSchema(part, value, path))
  }
  const alternatives = [...(schema.oneOf ?? []), ...(schema.anyOf ?? [])]
  if (alternatives.length > 0 && !alternatives.some((part) => {return checkAgainstSchema(part, value, path).length === 0})) {
    findings.push(`${path}: matches none of the ${alternatives.length.toString()} shapes the contract allows`)
  }

  const actual = typeOf(value)
  if (schema.type !== undefined && !typeAccepts(schema.type, actual)) {
    findings.push(`${path}: is ${actual} where the contract says ${schema.type}`)
    return findings
  }
  if (schema.enum !== undefined && !schema.enum.includes(value)) {
    findings.push(`${path}: ${JSON.stringify(value)} is not one of ${JSON.stringify(schema.enum)}`)
  }
  if (typeof value === 'string') {
    if (schema.format === 'uuid' && !UUID_PATTERN.test(value)) {
      findings.push(`${path}: "${value}" is not a UUID`)
    }
    if (schema.format === 'date-time' && Number.isNaN(Date.parse(value))) {
      findings.push(`${path}: "${value}" is not a date-time`)
    }
  }
  if (Array.isArray(value) && schema.items !== undefined) {
    const items = schema.items
    value.forEach((item, index) => {
      findings.push(...checkAgainstSchema(items, item, `${path}[${index.toString()}]`))
    })
  }
  if (actual === 'object') {
    findings.push(...checkObject(schema, value as Record<string, unknown>, path))
  }
  return findings.slice(0, MAX_FINDINGS)
}

/**
 * Checks an object's fields: the required ones are present, the present ones conform, and an
 * unknown one is refused when the contract forbids them.
 * @param schema The object schema.
 * @param value The parsed object.
 * @param path Where the object sits in the body.
 * @returns One sentence per disagreement.
 */
const checkObject = (schema: OpenApiSchema, value: Record<string, unknown>, path: string): string[] => {
  const findings: string[] = []
  const properties = schema.properties ?? {}
  for (const name of schema.required ?? []) {
    if (!(name in value)) {
      findings.push(`${path}.${name}: is missing but the contract requires it`)
    }
  }
  for (const [name, field] of Object.entries(value)) {
    const declared = properties[name]
    if (declared !== undefined) {
      findings.push(...checkAgainstSchema(declared, field, `${path}.${name}`))
    } else if (schema.additionalProperties === false) {
      findings.push(`${path}.${name}: is not a field of the contract`)
    } else if (typeof schema.additionalProperties === 'object') {
      findings.push(...checkAgainstSchema(schema.additionalProperties, field, `${path}.${name}`))
    }
  }
  return findings
}
