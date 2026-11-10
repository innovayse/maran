import type { Page, Route } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type { DnsRecordCatalog, DnsZone } from '../../src/types/dns'
import type { ZoneDnssec } from '../../src/types/dnsDnssec'
import type { DnsRecord, DnsRecordRequest, SaveDnsRecordRequest } from '../../src/types/dnsRecord'
import type { DnsServerStatus, DnsSettings } from '../../src/types/dnsSettings'

/** The whole DNS API, under which one handler dispatches by path. */
const DNS_API = /\/api\/v1\/dns-(zones|settings|server)/

/** The path segments that name a part of the API rather than a row, so the rest are identities. */
const LITERAL_SEGMENTS: ReadonlySet<string> = new Set([
  'dns-zones',
  'dns-settings',
  'dns-server',
  'records',
  'record-types',
  'enable',
  'disable',
  'dnssec',
  'templates',
  'choices',
])

/** The record types the panel serves, as `GET /api/v1/dns-zones/record-types` lists them. */
export const DNS_CATALOG: DnsRecordCatalog = {
  types: [
    { type: 'a', label: 'A', description: 'Points a name at an IPv4 address.', usesPriority: false, usesWeightAndPort: false, usesFlagsAndTag: false },
    { type: 'aaaa', label: 'AAAA', description: 'Points a name at an IPv6 address.', usesPriority: false, usesWeightAndPort: false, usesFlagsAndTag: false },
    { type: 'cname', label: 'CNAME', description: 'Makes a name an alias of another name.', usesPriority: false, usesWeightAndPort: false, usesFlagsAndTag: false },
    { type: 'mx', label: 'MX', description: 'Names the mail server of the zone.', usesPriority: true, usesWeightAndPort: false, usesFlagsAndTag: false },
    { type: 'txt', label: 'TXT', description: 'Holds text, such as an SPF policy.', usesPriority: false, usesWeightAndPort: false, usesFlagsAndTag: false },
    { type: 'srv', label: 'SRV', description: 'Locates a service by host and port.', usesPriority: true, usesWeightAndPort: true, usesFlagsAndTag: false },
    { type: 'caa', label: 'CAA', description: 'Limits who may issue certificates.', usesPriority: false, usesWeightAndPort: false, usesFlagsAndTag: true },
    { type: 'ns', label: 'NS', description: 'Delegates a part of the zone.', usesPriority: false, usesWeightAndPort: false, usesFlagsAndTag: false },
  ],
  minTtl: 60,
  maxTtl: 604800,
  defaultTtl: 3600,
  maxValueLength: 1024,
  maxRecordsPerZone: 500,
  caaTags: ['issue', 'issuewild', 'iodef'],
}

/** The settings the stub starts with: complete, and the name server turned on. */
export const DNS_SETTINGS: DnsSettings = {
  primaryNameserver: 'ns1.panel.example',
  secondaryNameserver: 'ns2.panel.example',
  hostmasterMailbox: 'hostmaster.panel.example',
  publicIpv4: '203.0.113.10',
  publicIpv6: '',
  defaultTtl: 3600,
  enabled: true,
  complete: true,
}

/** The name server's state the stub starts with: on and answering. */
export const DNS_SERVER: DnsServerStatus = {
  enabled: true,
  running: true,
  listening: true,
  version: '9.18.28',
  zoneCount: 2,
  agentReachable: true,
  port: 53,
  recursionRefused: 'inForce',
  responseRateLimiting: 'inForce',
}

/** What the panel answers about a zone's signing on a host that cannot sign. */
const DNSSEC_UNAVAILABLE: ZoneDnssec = {
  state: 'off',
  available: false,
  ds: null,
  dnskey: null,
  observed: null,
  withdrawalStartedAt: null,
  withdrawalEarliestFinishAt: null,
  rolloverAutomatic: false,
}

/** The names the panel gives the sources of a record. */
const ORIGIN_NAMES: Readonly<Record<string, string>> = {
  customer: 'Added by hand',
  template: 'From a template',
  system: 'Managed by the panel',
  mail: 'Managed by the mail facility',
  acmeChallenge: 'Managed by certificate issuing',
}

/** One refused request the stub was told to answer with a problem body. */
export interface DnsRefusal {
  /** The RFC 7807 `detail`, already localized the way the panel's own text is. */
  detail: string
  /** The status to answer with; 409 when left out. */
  status?: number
}

/** What a spec seeds the stub with, and reads back after the page acted. */
export interface DnsStubState {
  /** The zones the stub holds. */
  zones: DnsZone[]
  /** The records of each zone, by zone id. */
  records: Record<string, DnsRecord[]>
  /** The settings. */
  settings: DnsSettings
  /** The name server's state. */
  server: DnsServerStatus
  /** The record type catalogue. */
  catalog: DnsRecordCatalog
  /**
   * Requests to refuse, by `"<METHOD> <shape>"`, where the shape names the route with `:id` for
   * each identity (`POST dns-zones`, `PUT dns-zones/:id/records/:id`).
   */
  refusals: Record<string, DnsRefusal>
  /** Every request the stub saw, oldest first. */
  requests: { method: string; url: URL; body: unknown }[]
}

/** The paging rules of the zones endpoint. */
const ZONE_RULES: PagedRules<DnsZone> = {
  matches: (zone, term) => {
    return zone.name.toLowerCase().includes(term) || zone.accountName.toLowerCase().includes(term)
  },
  sorts: {
    name: (zone) => {
      return zone.name
    },
    createdAt: (zone) => {
      return zone.createdAt
    },
  },
  filters: {
    accountId: (zone, value) => {
      return zone.accountId === value
    },
  },
}

/** The paging rules of the records endpoint. */
const RECORD_RULES: PagedRules<DnsRecord> = {
  matches: (record, term) => {
    return record.name.toLowerCase().includes(term) || record.value.toLowerCase().includes(term)
  },
  sorts: {
    name: (record) => {
      return record.name
    },
    type: (record) => {
      return record.type
    },
    ttl: (record) => {
      return String(record.ttl).padStart(8, '0')
    },
  },
  filters: {
    type: (record, value) => {
      return record.type === value.toLowerCase()
    },
  },
}

/**
 * Completes a record the way a spec writes it: the fields a type does not take stay at zero and
 * the empty tag, which is how the panel stores them.
 * @param fields The fields the spec states.
 * @returns The complete record request.
 */
export const dnsDraft = (fields: Pick<DnsRecordRequest, 'name' | 'type' | 'value'> & Partial<DnsRecordRequest>): DnsRecordRequest => {
  return { ttl: 3600, priority: 0, weight: 0, port: 0, flags: 0, tag: '', ...fields }
}

/**
 * Derives the identity a record has on the wire from a readable handle, so a spec can name a record
 * `rec-www` while the panel's contract (a UUID) still holds and the same handle always gives the
 * same identity.
 * @param handle The readable name a spec gives the record.
 * @returns A version-4-shaped UUID.
 */
export const dnsRecordId = (handle: string): string => {
  // Two 32-bit FNV-1a runs with different seeds give twelve hex digits, enough to tell a spec's few
  // records apart.
  const run = (seed: number): string => {
    let hash = seed
    for (const character of handle) {
      hash = Math.imul(hash ^ (character.codePointAt(0) ?? 0), 0x01000193) >>> 0
    }
    return hash.toString(16).padStart(8, '0')
  }
  return `dec00000-0000-4000-8000-${run(0x811c9dc5)}${run(0x2545f491).slice(0, 4)}`
}

/**
 * Builds a record the way the panel answers one.
 * @param zoneId The zone it belongs to.
 * @param handle The readable name of the record; its identity is {@link dnsRecordId} of it.
 * @param request The record's fields.
 * @param origin The machine name of the record's source; only a record added by hand is editable.
 * @returns The record.
 */
export const dnsRecord = (zoneId: string, handle: string, request: DnsRecordRequest, origin = 'customer'): DnsRecord => {
  return {
    ...request,
    id: dnsRecordId(handle),
    zoneId,
    origin,
    originName: ORIGIN_NAMES[origin] ?? origin,
    editable: origin === 'customer',
    createdAt: '2026-08-01T10:00:00Z',
  }
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
    body: JSON.stringify({ code: 'DnsStubRefusal', title: 'Refused', detail }),
  })
}

/**
 * Installs one handler for the whole DNS API, backed by mutable state, so a spec seeds what the
 * panel holds, drives the page and reads back what changed. Each resource answers like the real
 * endpoint: a list is a page honouring `page`, `pageSize`, `search`, `sort` and its filters, a
 * missing zone is a 404 problem, and a request named in {@link DnsStubState.refusals} is refused
 * with the given sentence.
 * @param page The Playwright page whose network the route is installed on.
 * @param seed What the panel holds; anything left out starts empty or at its default.
 * @returns The live state.
 */
export const stubDns = async (page: Page, seed: Partial<DnsStubState> = {}): Promise<DnsStubState> => {
  const state: DnsStubState = {
    zones: [],
    records: {},
    settings: DNS_SETTINGS,
    server: DNS_SERVER,
    catalog: DNS_CATALOG,
    refusals: {},
    requests: [],
    // A copy: a test changes the live state (a zone's serial after an edit), and the seed's
    // objects are shared fixtures every other test in the worker reads.
    ...structuredClone(seed),
  }

  await page.route(DNS_API, async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const method = request.method()
    const body: unknown = request.postData() === null ? undefined : request.postDataJSON()
    state.requests.push({ method, url, body })

    const parts = url.pathname.replace('/api/v1/', '').split('/')
    const shape = parts
      .map((part) => {
        return LITERAL_SEGMENTS.has(part) ? part : ':id'
      })
      .join('/')
    const refusal = state.refusals[`${method} ${shape}`]
    if (refusal !== undefined) {
      await problem(route, refusal.detail, refusal.status)
      return
    }

    /**
     * Answers 404 the way the panel does for a resource that is not the caller's.
     * @returns Resolves once the response is sent.
     */
    const notFound = (): Promise<void> => {
      return problem(route, 'There is no such resource.', 404)
    }
    const rowId = parts[1] ?? ''
    const zone = state.zones.find((candidate) => {
      return candidate.id === rowId
    })

    switch (`${method} ${shape}`) {
      case 'GET dns-zones/record-types':
        return json(route, state.catalog)
      case 'GET dns-zones':
        return json(route, pagedAnswer(state.zones, request.url(), ZONE_RULES))
      case 'POST dns-zones': {
        const created = body as { accountId: string; name: string }
        const made: DnsZone = {
          id: '99999999-9999-9999-9999-999999999999',
          accountId: created.accountId,
          accountName: 'alice',
          name: created.name,
          serial: 2026110701,
          recordCount: 0,
          createdAt: '2026-11-01T10:00:00Z',
          updatedAt: '2026-11-01T10:00:00Z',
          nameservers: [state.settings.primaryNameserver],
          dnssecState: 'off',
        }
        state.zones.push(made)
        return json(route, made, 201)
      }
      case 'GET dns-zones/templates/choices':
        // No template is offered unless a spec installs the templates stub after this one.
        return json(route, [])
      case 'GET dns-zones/:id/dnssec':
        // A host that cannot sign: the zone page offers no DNSSEC pane unless a spec installs the
        // DNSSEC stub after this one.
        return json(route, DNSSEC_UNAVAILABLE)
      case 'GET dns-zones/:id':
        return zone === undefined ? notFound() : json(route, zone)
      case 'DELETE dns-zones/:id':
        state.zones = state.zones.filter((candidate) => {
          return candidate.id !== rowId
        })
        return route.fulfill({ status: 204 })
      case 'GET dns-zones/:id/records':
        return json(route, pagedAnswer(state.records[rowId] ?? [], request.url(), RECORD_RULES))
      case 'POST dns-zones/:id/records': {
        const held = (state.records[rowId] ??= [])
        const made = dnsRecord(rowId, `rec-${(held.length + 1).toString()}`, (body as SaveDnsRecordRequest).record)
        held.push(made)
        return json(route, made, 201)
      }
      case 'PUT dns-zones/:id/records/:id': {
        const held = state.records[rowId] ?? []
        const index = held.findIndex((candidate) => {
          return candidate.id === parts[3]
        })
        if (index < 0) {
          return notFound()
        }
        const changed: DnsRecord = { ...dnsRecord(rowId, '', (body as SaveDnsRecordRequest).record), id: parts[3] ?? '' }
        held[index] = changed
        return json(route, changed)
      }
      case 'DELETE dns-zones/:id/records/:id':
        state.records[rowId] = (state.records[rowId] ?? []).filter((candidate) => {
          return candidate.id !== parts[3]
        })
        return route.fulfill({ status: 204 })
      case 'GET dns-settings':
        return json(route, state.settings)
      case 'PUT dns-settings':
        state.settings = { ...state.settings, ...(body as Partial<DnsSettings>) }
        return json(route, state.settings)
      case 'GET dns-server':
        return json(route, state.server)
      case 'POST dns-server/enable':
        state.server = { ...state.server, enabled: true, running: true, listening: true }
        return json(route, state.server)
      case 'POST dns-server/disable':
        state.server = { ...state.server, enabled: false, running: false, listening: false }
        return json(route, state.server)
      default:
        return notFound()
    }
  })

  return state
}
