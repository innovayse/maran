import type { DnsRecordRequest } from './dnsRecord'
import type { ListRequest, Paged } from './list'

/**
 * DNS zone templates as the panel sends and takes them (`/api/v1/dns-zone-templates`, issue #452):
 * a named set of records a new zone starts with. A record's value may carry the variables the
 * panel's catalogue names (`{apex}`, `{ipv4}`); the panel expands them when a zone is created.
 * Enums arrive as camelCase strings and every sentence a person reads arrives already localized.
 */

/** One template as the list shows it, mirroring the backend's `ZoneTemplateDto`. */
export interface DnsTemplate {
  /** The template's identity. */
  id: string
  /** The template's name. */
  name: string
  /** What the template is for, in a sentence; empty when none was written. */
  description: string
  /** Whether the panel ships the template: it can be changed but not deleted. */
  isBuiltIn: boolean
  /** Whether zones created from a site start from this template. */
  isDefault: boolean
  /** How many records the template holds. */
  recordCount: number
  /** The instant the template last changed, as an ISO-8601 string. */
  updatedAt: string
}

/**
 * A record of a template, mirroring the backend's `ZoneTemplateRecordDto`: the shape of a zone's
 * record request, except that the TTL may be absent and the zone then takes the settings' default.
 */
export interface DnsTemplateRecord extends Omit<DnsRecordRequest, 'ttl'> {
  /** Time to live in seconds, or `null` for the default TTL of the name server settings. */
  ttl: number | null
}

/** One template with its records, mirroring the backend's `ZoneTemplateDetailDto`. */
export interface DnsTemplateDetail extends Omit<DnsTemplate, 'recordCount'> {
  /** The records a zone created from the template starts with, in order. */
  records: DnsTemplateRecord[]
}

/** What `POST` and `PUT /api/v1/dns-zone-templates` take, the backend's `SaveZoneTemplateCommand`. */
export interface SaveDnsTemplateRequest {
  /** The template's name. */
  name: string
  /** What the template is for; may be empty. */
  description: string
  /** The records of the template; replaces the stored set whole. */
  records: DnsTemplateRecord[]
}

/** One variable a record value may carry, from `GET /api/v1/dns-zone-templates/variables`. */
export interface DnsTemplateVariable {
  /** The token as it is written in a value, braces included. */
  token: string
  /** What the token stands for and where it may be used, in the reader's language. */
  description: string
}

/** One template a new zone can start from, from `GET /api/v1/dns-zones/templates/choices`. */
export interface DnsTemplateChoice {
  /** The template's identity, sent as `templateId` when the zone is created. */
  id: string
  /** The template's name. */
  name: string
  /** Whether the panel starts new zones from it unless told otherwise. */
  isDefault: boolean
}

/**
 * Where the SPA's read of one template stands, so "no such template" and "the read failed" are
 * different cases and neither is a null beside a message.
 */
export type DnsTemplateLoad =
  | {
      /** Nothing has been asked for yet, or the question is in flight. */
      state: 'loading'
    }
  | {
      /** The panel answered with the template. */
      state: 'found'
      /** The template with its records. */
      template: DnsTemplateDetail
    }
  | {
      /** The panel answered 404: no such template. */
      state: 'notFound'
    }
  | {
      /** The panel refused for another reason, or did not answer. */
      state: 'failed'
      /** The panel's already-localized reason, or `null` when it did not answer at all. */
      message: string | null
    }

/** Typed access to the template endpoints. Called from Pinia stores only (rules/vue.md). */
export interface DnsTemplatesApi {
  /**
   * Lists one page of the templates; administrators only.
   * @param request The page, size, search and sort.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered.
   */
  list: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<DnsTemplate>>

  /**
   * Reads one template with its records.
   * @param id The template to read.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The template.
   */
  get: (id: string, signal?: AbortSignal) => Promise<DnsTemplateDetail>

  /**
   * Creates a template.
   * @param request The name, description and records.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The template as created.
   */
  create: (request: SaveDnsTemplateRequest, signal?: AbortSignal) => Promise<DnsTemplateDetail>

  /**
   * Replaces a template whole.
   * @param id The template.
   * @param request The template as it should now be.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The template as saved.
   */
  update: (id: string, request: SaveDnsTemplateRequest, signal?: AbortSignal) => Promise<DnsTemplateDetail>

  /**
   * Deletes a template; the panel refuses a built-in or default one.
   * @param id The template.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel deleted it (an empty 204 answer).
   */
  remove: (id: string, signal?: AbortSignal) => Promise<void>

  /**
   * Makes a template the one zones created from a site start from.
   * @param id The template.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel changed the default (an empty 204 answer).
   */
  makeDefault: (id: string, signal?: AbortSignal) => Promise<void>

  /**
   * Reads the variable catalogue a record value may use.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The tokens with their descriptions.
   */
  variables: (signal?: AbortSignal) => Promise<DnsTemplateVariable[]>

  /**
   * Lists the templates a new zone can start from, for any caller who may create a zone.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The choices, never the records.
   */
  choices: (signal?: AbortSignal) => Promise<DnsTemplateChoice[]>
}
