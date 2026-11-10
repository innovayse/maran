import type { Page, Route } from '@playwright/test'
import { openDnsScreen } from './open-dns-screen'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type { DnsStubState } from './stub-dns-routes'
import type { AuthenticatedUser } from '../../src/types/auth'
import type {
  DnsTemplate,
  DnsTemplateChoice,
  DnsTemplateDetail,
  DnsTemplateRecord,
  DnsTemplateVariable,
  SaveDnsTemplateRequest,
} from '../../src/types/dnsTemplate'

/** The two APIs the templates screens read: the administrator's templates and the create form's choices. */
const TEMPLATES_API = /\/api\/v1\/(dns-zone-templates|dns-zones\/templates\/choices)/

/** The identity the stub gives the template a spec creates. */
export const CREATED_TEMPLATE_ID = 'cccccccc-cccc-cccc-cccc-cccccccccccc'

/** The variable catalogue the stub answers, in the panel's wording; the SPA holds none of it. */
export const DNS_TEMPLATE_VARIABLES: DnsTemplateVariable[] = [
  { token: '{apex}', description: 'The zone name, such as example.com.' },
  { token: '{ipv4}', description: 'The public IPv4 address of this server.' },
  { token: '{ipv6}', description: 'The public IPv6 address of this server.' },
]

/**
 * Completes a template record the way a spec writes it.
 * @param fields The fields the spec states.
 * @returns The complete template record.
 */
export const templateRecord = (
  fields: Pick<DnsTemplateRecord, 'name' | 'type' | 'value'> & Partial<DnsTemplateRecord>,
): DnsTemplateRecord => {
  return { ttl: null, priority: 0, weight: 0, port: 0, flags: 0, tag: '', ...fields }
}

/** The built-in default template of the stub. */
export const WEB_TEMPLATE: DnsTemplateDetail = {
  id: '11111111-aaaa-aaaa-aaaa-111111111111',
  name: 'web',
  description: 'A website on this server.',
  isBuiltIn: true,
  isDefault: true,
  updatedAt: '2026-11-01T10:00:00Z',
  records: [
    templateRecord({ name: '@', type: 'a', value: '{ipv4}' }),
    templateRecord({ name: 'www', type: 'cname', value: '{apex}.' }),
  ],
}

/** A built-in template that is not the default. */
export const WEB_AND_MAIL_TEMPLATE: DnsTemplateDetail = {
  id: '22222222-aaaa-aaaa-aaaa-222222222222',
  name: 'web and mail',
  description: 'A website and its mail.',
  isBuiltIn: true,
  isDefault: false,
  updatedAt: '2026-11-01T10:00:00Z',
  records: [
    templateRecord({ name: '@', type: 'a', value: '{ipv4}' }),
    templateRecord({ name: '@', type: 'mx', value: 'mail.{apex}', priority: 10, ttl: 3600 }),
  ],
}

/** A template an administrator made: neither built in nor the default, so it can be deleted. */
export const CUSTOM_TEMPLATE: DnsTemplateDetail = {
  id: '33333333-aaaa-aaaa-aaaa-333333333333',
  name: 'parking',
  description: 'A parked domain.',
  isBuiltIn: false,
  isDefault: false,
  updatedAt: '2026-11-05T10:00:00Z',
  records: [templateRecord({ name: '@', type: 'txt', value: 'parked' })],
}

/** What a spec seeds the templates stub with, and reads back after the page acted. */
export interface DnsTemplatesStubState {
  /** The templates the stub holds, with their records. */
  templates: DnsTemplateDetail[]
  /** The variable catalogue. */
  variables: DnsTemplateVariable[]
  /** Requests to refuse, by `"<METHOD> <shape>"` (`POST templates`, `DELETE templates/:id`), with the sentence to answer. */
  refusals: Record<string, string>
  /** Every request the stub saw, oldest first. */
  requests: { method: string; url: URL; body: unknown }[]
}

/** The paging rules of the templates endpoint. */
const TEMPLATE_RULES: PagedRules<DnsTemplate> = {
  matches: (template, term) => {
    return template.name.toLowerCase().includes(term)
  },
  sorts: {
    name: (template) => {
      return template.name
    },
    updatedAt: (template) => {
      return template.updatedAt
    },
  },
  filters: {},
}

/**
 * Answers a request with a JSON body.
 * @param route The intercepted request.
 * @param body The body.
 * @param status The status; 200 when left out.
 * @returns Resolves once the response is sent.
 */
const json = async (route: Route, body: unknown, status = 200): Promise<void> => {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
}

/**
 * Answers a request with an RFC 7807 problem body, the shape the SPA renders verbatim.
 * @param route The intercepted request.
 * @param detail The panel's localized sentence.
 * @param status The status; 409 when left out.
 * @returns Resolves once the response is sent.
 */
const problem = async (route: Route, detail: string, status = 409): Promise<void> => {
  await route.fulfill({
    status,
    contentType: 'application/problem+json',
    body: JSON.stringify({ code: 'DnsTemplateStubRefusal', title: 'Refused', detail }),
  })
}

/**
 * Names the first token in a record value that the catalogue does not list, the way the panel
 * refuses an unknown variable when a template is saved.
 * @param records The records of the request.
 * @param known The catalogue's tokens.
 * @returns The offending token, or `null` when every one is listed.
 */
const unknownToken = (records: DnsTemplateRecord[], known: string[]): string | null => {
  for (const record of records) {
    for (const match of record.value.matchAll(/\{[^{}]*\}/g)) {
      if (!known.includes(match[0])) {
        return match[0]
      }
    }
  }
  return null
}

/**
 * Reads a detail as the list shows it.
 * @param template The detail.
 * @returns The list row.
 */
const summaryOf = (template: DnsTemplateDetail): DnsTemplate => {
  const { records, ...rest } = template
  return { ...rest, recordCount: records.length }
}

/**
 * Installs the templates API on a page: the administrator's endpoints and the create-zone form's
 * choices, backed by mutable state, so a spec seeds what the panel holds, drives the page and reads
 * back what changed. Install it AFTER {@link openDnsScreen}: a route registered later is asked first,
 * and the zones stub would otherwise answer the choices path with a 404.
 * @param page The Playwright page whose network the routes are installed on.
 * @param seed What the panel holds; anything left out starts at its default.
 * @returns The live state.
 */
export const stubDnsTemplates = async (
  page: Page,
  seed: Partial<DnsTemplatesStubState> = {},
): Promise<DnsTemplatesStubState> => {
  const state: DnsTemplatesStubState = {
    templates: [],
    variables: DNS_TEMPLATE_VARIABLES,
    refusals: {},
    requests: [],
    // A copy: a spec changes the live state, and the seed's objects are shared fixtures.
    ...structuredClone(seed),
  }

  await page.route(TEMPLATES_API, async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const method = request.method()
    const body: unknown = request.postData() === null ? undefined : request.postDataJSON()
    state.requests.push({ method, url, body })

    if (url.pathname.endsWith('/dns-zones/templates/choices')) {
      const choices: DnsTemplateChoice[] = state.templates.map((template) => {
        return { id: template.id, name: template.name, isDefault: template.isDefault }
      })
      return json(route, choices)
    }

    const parts = url.pathname.replace('/api/v1/dns-zone-templates', '').split('/').filter((part) => { return part !== '' })
    const shape = ['templates', ...parts.map((part) => { return part === 'variables' || part === 'make-default' ? part : ':id' })].join('/')
    const refusal = state.refusals[`${method} ${shape}`]
    if (refusal !== undefined) {
      return problem(route, refusal)
    }
    const found = state.templates.find((candidate) => { return candidate.id === parts[0] })

    switch (`${method} ${shape}`) {
      case 'GET templates':
        return json(route, pagedAnswer(state.templates.map(summaryOf), request.url(), TEMPLATE_RULES))
      case 'GET templates/variables':
        return json(route, state.variables)
      case 'GET templates/:id':
        return found === undefined ? problem(route, 'There is no such template.', 404) : json(route, found)
      case 'POST templates':
      case 'PUT templates/:id': {
        const saved = body as SaveDnsTemplateRequest
        const bad = unknownToken(saved.records, state.variables.map((variable) => { return variable.token }))
        if (bad !== null) {
          return problem(route, `The variable ${bad} is not one of the variables a template may use.`, 400)
        }
        if (method === 'PUT' && found === undefined) {
          return problem(route, 'There is no such template.', 404)
        }
        const made: DnsTemplateDetail = {
          id: found?.id ?? CREATED_TEMPLATE_ID,
          name: saved.name,
          description: saved.description,
          isBuiltIn: found?.isBuiltIn ?? false,
          isDefault: found?.isDefault ?? false,
          updatedAt: '2026-11-09T10:00:00Z',
          records: saved.records,
        }
        state.templates = found === undefined ? [...state.templates, made] : state.templates.map((t) => { return t.id === made.id ? made : t })
        return json(route, made, method === 'POST' ? 201 : 200)
      }
      case 'DELETE templates/:id':
        state.templates = state.templates.filter((candidate) => { return candidate.id !== parts[0] })
        return route.fulfill({ status: 204 })
      case 'POST templates/:id/make-default':
        state.templates = state.templates.map((candidate) => { return ({ ...candidate, isDefault: candidate.id === parts[0] }) })
        return route.fulfill({ status: 204 })
      default:
        return problem(route, 'There is no such resource.', 404)
    }
  })

  return state
}

/**
 * Signs a person in and installs both the DNS stubs and the templates stub.
 * @param page The Playwright page under test.
 * @param templates What the panel holds for templates.
 * @param zones What the panel holds for zones and the rest of DNS.
 * @param user The signed-in user; the administrator when omitted.
 * @returns Both live states.
 */
export const openDnsTemplatesScreen = async (
  page: Page,
  templates: Partial<DnsTemplatesStubState> = {},
  zones: Partial<DnsStubState> = {},
  user?: AuthenticatedUser,
): Promise<{ dns: DnsStubState; templates: DnsTemplatesStubState }> => {
  const dns = await openDnsScreen(page, zones, user)
  return { dns, templates: await stubDnsTemplates(page, templates) }
}
