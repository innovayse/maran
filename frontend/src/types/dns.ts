import type { DnsDnssecState } from './dnsDnssec'
import type { DnsRecordTypeKey } from './dnsRecord'
import type { ListRequest, Paged } from './list'

/**
 * The DNS module's zones and its reference data, as the panel sends them
 * (`/api/v1/dns-zones/...`, issue #178). Enums arrive as camelCase strings (panel-wide
 * `JsonStringEnumConverter`) and every name a person reads arrives already localized.
 *
 * Records and settings are separate domains with their own files: `dnsRecord.ts` and `dnsSettings.ts`.
 */

/**
 * The inputs a record type takes beyond its value, as the form names them. Which of them a type
 * takes is the panel's answer (`usesPriority`, `usesWeightAndPort`, `usesFlagsAndTag` of the record
 * type catalogue); the form never decides which type has a port.
 */
export type DnsRecordField = 'priority' | 'weight' | 'port' | 'flags' | 'tag'

/** One record type the panel can serve, mirroring the backend's `RecordTypeDto`. */
export interface DnsRecordTypeInfo {
  /** The type on the wire (`a`, `mx`), camelCase; also the value sent on a record. */
  type: DnsRecordTypeKey
  /** The type as it is written in a zone (`A`, `MX`). */
  label: string
  /** What the type is for, in one sentence in the reader's language. */
  description: string
  /** Whether the type takes a priority (an MX preference, an SRV priority). */
  usesPriority: boolean
  /** Whether the type takes a weight and a port (an SRV). */
  usesWeightAndPort: boolean
  /** Whether the type takes flags and a tag (a CAA). */
  usesFlagsAndTag: boolean
}

/**
 * The reference data every DNS screen selects from, mirroring the backend's `RecordTypeCatalogDto`
 * (`GET /api/v1/dns-zones/record-types`): the record types and the bounds the server enforces. The
 * SPA holds none of it as constants.
 */
export interface DnsRecordCatalog {
  /** The record types the panel serves, in display order. */
  types: DnsRecordTypeInfo[]
  /** The shortest TTL in seconds the server accepts. */
  minTtl: number
  /** The longest TTL in seconds the server accepts. */
  maxTtl: number
  /** The TTL a new record starts with. */
  defaultTtl: number
  /** The longest record value in characters. */
  maxValueLength: number
  /** The most records a zone may hold. */
  maxRecordsPerZone: number
  /** The CAA tags the panel accepts, from its allow-list. */
  caaTags: string[]
}

/** Outward view of a zone, mirroring the backend's `ZoneDto`. */
export interface DnsZone {
  /** The zone's identity. */
  id: string
  /** The account that owns the zone. */
  accountId: string
  /** The owning account's short name, resolved by the panel. */
  accountName: string
  /** The zone's apex name (`example.com`). */
  name: string
  /** The zone's SOA serial. */
  serial: number
  /** How many records the zone holds, not counting the system-managed SOA and apex NS. */
  recordCount: number
  /** The instant the zone was created, as an ISO-8601 string. */
  createdAt: string
  /** The instant the zone last changed, as an ISO-8601 string. */
  updatedAt: string
  /**
   * The name servers the zone names, which the owner delegates the domain to at the registrar;
   * empty while none is stated.
   */
  nameservers: string[]
  /** Where the zone's DNSSEC signing stands, so a list or a delete confirmation can warn about the DS record. */
  dnssecState: DnsDnssecState
}

/** What `POST /api/v1/dns-zones` takes. */
export interface CreateDnsZoneRequest {
  /** The account that will own the zone. */
  accountId: string
  /** The apex name. The panel accepts only a name one of the account's sites claims. */
  name: string
  /** The template the zone starts from, or `null` for an empty zone. */
  templateId: string | null
}

/**
 * Where the SPA's read of a zone stands, so "no such zone" and "the read failed" are different
 * cases and neither is a null beside a message.
 */
export type DnsZoneLoad =
  | {
      /** Nothing has been asked for yet, or the question is in flight. */
      state: 'loading'
    }
  | {
      /** The panel answered with the zone. */
      state: 'found'
      /** The zone. */
      zone: DnsZone
    }
  | {
      /** The panel answered 404: no such zone for the caller. */
      state: 'notFound'
    }
  | {
      /** The panel refused for another reason, or did not answer. */
      state: 'failed'
      /** The panel's already-localized reason, or `null` when it did not answer at all. */
      message: string | null
    }

/** Typed access to the zone endpoints. Called from Pinia stores only (rules/vue.md). */
export interface DnsZonesApi {
  /**
   * Reads the record types and the bounds the server enforces.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The catalogue.
   */
  catalog: (signal?: AbortSignal) => Promise<DnsRecordCatalog>

  /**
   * Lists one page of the zones the caller may see. Another account's zones are not in the answer.
   * @param request The page, size, search, sort and the account filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered.
   */
  list: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<DnsZone>>

  /**
   * Reads one zone. A zone another account owns answers 404, never 403.
   * @param id The zone to read.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The zone.
   */
  get: (id: string, signal?: AbortSignal) => Promise<DnsZone>

  /**
   * Creates a zone.
   * @param request The owning account and the apex name.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The zone as created.
   */
  create: (request: CreateDnsZoneRequest, signal?: AbortSignal) => Promise<DnsZone>

  /**
   * Deletes a zone and every record in it; the name server stops answering for the name.
   * @param id The zone to delete.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel deleted the zone (an empty 204 answer).
   */
  remove: (id: string, signal?: AbortSignal) => Promise<void>
}
