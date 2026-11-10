import type { DnsPolicyState } from './dnsSettings'

/**
 * A zone's DNSSEC signing, as the panel sends it
 * (`/api/v1/dns-zones/{id}/dnssec`, issue #452). Enums arrive as camelCase strings and every
 * sentence a person reads arrives already localized. The private key is never part of any answer.
 */

/**
 * Where a zone's signing stands, mirroring the backend's `ZoneDnssecDto.state`:
 * `off` serves the zone unsigned; `signing` serves it signed; `withdrawing` serves it unsigned
 * while the registrar may still hold the DS record, until the owner confirms its removal.
 */
export type DnsDnssecState = 'off' | 'signing' | 'withdrawing'

/** The DS record the owner publishes at the registrar so resolvers can validate the zone. */
export interface DnsDsRecord {
  /** The key tag of the signing key. */
  keyTag: number
  /** The DNSSEC algorithm number (13 for ECDSA P-256). */
  algorithm: number
  /** The digest type number (2 for SHA-256). */
  digestType: number
  /** The digest, in hexadecimal. */
  digest: string
}

/** The public DNSKEY of the signing key, for registrars that ask for the key rather than the DS. */
export interface DnsDnskeyRecord {
  /** The DNSKEY flags (257 for a key-signing key). */
  flags: number
  /** The DNSKEY protocol field (always 3). */
  protocol: number
  /** The DNSSEC algorithm number. */
  algorithm: number
  /** The public key, base64. */
  publicKey: string
}

/** What the name server reported about the zone's signing, so a stored state is checked against the running one. */
export interface DnsDnssecObservation {
  /** The signing mode the panel wants the name server to be in. */
  mode: DnsDnssecState
  /** Whether the name server was seen in that mode; `notObserved` is never read as `inForce`. */
  modeObserved: DnsPolicyState
  /** The key tag the name server publishes, or `0` while none was seen. */
  publishedKeyTag: number
  /** Whether the name server was seen publishing the stored key. */
  publishedKeyObserved: DnsPolicyState
}

/** Outward view of a zone's DNSSEC signing, mirroring the backend's `ZoneDnssecDto`. */
export interface ZoneDnssec {
  /** Where the signing stands. */
  state: DnsDnssecState
  /** Whether this host can sign zones at all; the tab is hidden when it cannot and the zone is `off`. */
  available: boolean
  /** The DS record to publish at the registrar, or `null` while the zone has no key. */
  ds: DnsDsRecord | null
  /** The public key, or `null` while the zone has no key. */
  dnskey: DnsDnskeyRecord | null
  /** What the name server reported, or `null` when it was not asked. */
  observed: DnsDnssecObservation | null
  /** When the withdrawal began, as an ISO-8601 string, or `null` outside a withdrawal. */
  withdrawalStartedAt: string | null
  /** The earliest instant the withdrawal can be finished (the server enforces it), or `null` outside a withdrawal. */
  withdrawalEarliestFinishAt: string | null
  /** Whether keys roll over by themselves; always `false` in v1, and the screen says so. */
  rolloverAutomatic: boolean
}

/** What `POST /api/v1/dns-zones/{id}/dnssec/finish-withdrawal` takes. */
export interface FinishDnssecWithdrawalRequest {
  /** The owner's statement that the DS record was removed at the registrar; the server refuses unless it is `true`. */
  dsRecordRemoved: boolean
}

/**
 * Where the SPA's read of a zone's signing stands, so "not read yet" and "the read failed" are
 * different cases and neither is a null beside a message.
 */
export type DnsDnssecLoad =
  | {
      /** Nothing has been asked for yet, or the question is in flight. */
      state: 'loading'
    }
  | {
      /** The panel answered. */
      state: 'loaded'
      /** The signing. */
      dnssec: ZoneDnssec
    }
  | {
      /** The panel refused, or did not answer. */
      state: 'failed'
      /** The panel's already-localized reason, or `null` when it did not answer at all. */
      message: string | null
    }

/** Typed access to the DNSSEC endpoints. Called from Pinia stores only (rules/vue.md). */
export interface DnsDnssecApi {
  /**
   * Reads a zone's signing. A zone another account owns answers 404, never 403.
   * @param zoneId The zone.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The signing.
   */
  get: (zoneId: string, signal?: AbortSignal) => Promise<ZoneDnssec>

  /**
   * Starts signing the zone: the panel generates the key and the name server serves the zone signed.
   * @param zoneId The zone.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The signing as it stands afterwards.
   */
  enable: (zoneId: string, signal?: AbortSignal) => Promise<ZoneDnssec>

  /**
   * Begins withdrawing the signing: the zone is served unsigned while the DS record is removed at the registrar.
   * @param zoneId The zone.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The signing as it stands afterwards.
   */
  withdraw: (zoneId: string, signal?: AbortSignal) => Promise<ZoneDnssec>

  /**
   * Ends the withdrawal and deletes the key. The panel refuses before its earliest instant.
   * @param zoneId The zone.
   * @param request The owner's statement that the DS record is gone.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The signing as it stands afterwards.
   */
  finishWithdrawal: (zoneId: string, request: FinishDnssecWithdrawalRequest, signal?: AbortSignal) => Promise<ZoneDnssec>
}
