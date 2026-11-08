import type { ListRequest, Paged } from './list'

/**
 * A zone's records as the panel sends and takes them (`/api/v1/dns-zones/{id}/records`, issue
 * #178). One typed column per meaning: the priority, weight, port, flags and tag are columns of
 * their own, never packed into the value.
 */

/**
 * A record type as the panel names it on the wire: the camelCase member name of the backend's
 * `RecordType` enum. The union is the request contract and nothing more, so `maran api` can tell a
 * renamed type from a typo. The SPA offers no list of its own: the types a form lists, their order,
 * labels and what each takes arrive from `GET /api/v1/dns-zones/record-types`, and a type the panel
 * adds is added to this union in the same change.
 */
export type DnsRecordTypeKey = 'a' | 'aaaa' | 'cname' | 'mx' | 'txt' | 'srv' | 'caa' | 'ns'

/**
 * What a person types to create or change a record, mirroring the backend's `RecordDraft`. Also
 * what a zone template stores. A field the record's type does not take stays at `0` (or the empty
 * string for the tag): the panel refuses a value there, so every field has one meaning.
 */
export interface DnsRecordRequest {
  /** The owner name relative to the zone: `@` for the apex, `www`, `*`, `_dmarc`. */
  name: string
  /** The record type on the wire (`a`, `mx`), one of the catalogue's. */
  type: DnsRecordTypeKey
  /** Time to live in seconds. */
  ttl: number
  /** The record's value: an address, a target name or text. */
  value: string
  /** The preference of an MX or the priority of an SRV; `0` for a type that has none. */
  priority: number
  /** The weight of an SRV; `0` for a type that has none. */
  weight: number
  /** The port of an SRV; `0` for a type that has none. */
  port: number
  /** The flags of a CAA; `0` for a type that has none. */
  flags: number
  /** The tag of a CAA; the empty string for a type that has none. */
  tag: string
}

/**
 * What `POST .../records` and `PUT .../records/{id}` take: the record inside a `record` member, the
 * shape of the backend's `AddRecordCommand` and `ChangeRecordCommand`. The zone and the record come
 * from the route.
 */
export interface SaveDnsRecordRequest {
  /** The record as it should read. */
  record: DnsRecordRequest
}

/** A record as a table row shows it: the request plus what the panel decided about it. */
export interface DnsRecordRow extends DnsRecordRequest {
  /** The row's identity. */
  id: string
  /** Whether the caller may change or delete it; only a record added by hand is. */
  editable: boolean
  /** Where the record came from (added by hand, from a template, managed by the panel) as a person reads it. */
  originName: string
}

/** A record of a zone, mirroring the backend's `ZoneRecordDto`. */
export interface DnsRecord extends DnsRecordRow {
  /** The zone the record belongs to. */
  zoneId: string
  /** The machine name of the record's source. */
  origin: string
  /** The instant the record was created, as an ISO-8601 string. */
  createdAt: string
}

/** Typed access to the record endpoints. Called from Pinia stores only (rules/vue.md). */
export interface DnsRecordsApi {
  /**
   * Lists one page of a zone's records.
   * @param zoneId The zone.
   * @param request The page, size, search, sort and the type filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered.
   */
  list: (zoneId: string, request: ListRequest, signal?: AbortSignal) => Promise<Paged<DnsRecord>>

  /**
   * Adds a record to a zone.
   * @param zoneId The zone.
   * @param request The record.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The record as created.
   */
  create: (zoneId: string, request: DnsRecordRequest, signal?: AbortSignal) => Promise<DnsRecord>

  /**
   * Replaces a record's fields.
   * @param zoneId The zone.
   * @param recordId The record.
   * @param request The record as it should now be.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The record as changed.
   */
  update: (zoneId: string, recordId: string, request: DnsRecordRequest, signal?: AbortSignal) => Promise<DnsRecord>

  /**
   * Deletes a record.
   * @param zoneId The zone.
   * @param recordId The record.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel deleted the record (an empty 204 answer).
   */
  remove: (zoneId: string, recordId: string, signal?: AbortSignal) => Promise<void>
}
