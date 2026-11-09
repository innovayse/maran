import type { ListRequest, Paged } from './list'

/**
 * The state an API key is in, as the panel derives it on every read: a key is `revoked` once an
 * administrator revoked it, `expired` once its expiry passed, `active` otherwise. The name the
 * interface prints for it is `ApiKey.statusName`, sent by the panel on every row, never written here.
 */
export type ApiKeyStatus = 'active' | 'revoked' | 'expired'

/**
 * Outward view of one API key, mirroring the backend's `ApiKeyDto` field-for-field. There is no
 * secret and no digest here: the plaintext exists in exactly one response, the one that created
 * the key, and the panel keeps only a hash.
 */
export interface ApiKey {
  /** The key's identity, and the only identifier a request may name. */
  id: string
  /** The label the administrator gave it. */
  name: string
  /** The public part of the key (8 hex characters), shown so a key can be told apart. */
  prefix: string
  /** The scope values the key was granted. */
  scopes: string[]
  /** The ranges the key may be used from; empty when it may be used from anywhere. */
  allowedCidrs: string[]
  /** When the key was created, as an ISO-8601 instant. */
  createdAt: string
  /** When the key stops working, or `null` when it never expires. */
  expiresAt: string | null
  /** When the key last authenticated a request, or `null` when it never has. */
  lastUsedAt: string | null
  /** The address of that last request, or `null` when the key was never used. */
  lastUsedIp: string | null
  /** When the key was revoked, or `null` while it has not been. */
  revokedAt: string | null
  /** The state the panel derived for this read. */
  status: ApiKeyStatus
  /** The panel's name for {@link status}, already in the request's language; the table prints this. */
  statusName: string
}

/**
 * Request body for `POST /api/v1/api-keys`, binding the backend's `CreateApiKeyCommand`
 * field-for-field.
 */
export interface CreateApiKeyRequest {
  /** The label; at most 64 characters. */
  name: string
  /** The scope values to grant, chosen from the list the panel offers. */
  scopes: string[]
  /** The ranges the key may be used from, or `null` for no restriction. */
  allowedCidrs: string[] | null
  /** When the key stops working as an ISO-8601 instant, or `null` for never. */
  expiresAt: string | null
}

/** What `POST /api/v1/api-keys` answered: the stored key and its plaintext, shown once. */
export interface ApiKeyCreated {
  /** The key as stored. */
  key: ApiKey
  /** The full secret, `mk_<prefix>_<token>`. It exists in this one response and nowhere else. */
  secret: string
}

/**
 * The secret on screen: the key's name and its plaintext. Held in memory by the store for exactly
 * one showing, and never written to storage, the URL or the console.
 */
export interface RevealedApiKey {
  /** The label of the key, so the dialog can say which key this is. */
  name: string
  /** The full secret. */
  secret: string
}

/** One value the panel names, for a list a form or a filter offers (a scope, a status). */
export interface ApiKeyChoice {
  /** The value as the wire spells it. */
  value: string
  /** The panel's name for it, already in the request's language. */
  name: string
}

/**
 * Whose keys a screen manages. Each case is a different endpoint family with a different authority
 * behind it, and the backend refuses by role regardless of which one the SPA names (rules/security.md
 * item 6), so this only picks the endpoint to call.
 *
 * - `administrator`: the administrator's own keys.
 * - `reseller`: the signed-in reseller's own keys.
 * - `overseen`: one reseller's keys, seen by the administrator from that reseller's page. It lists and
 *   revokes; it cannot create, so it is not an {@link IssuingApiKeyOwner}.
 */
export type ApiKeyOwner = IssuingApiKeyOwner | { kind: 'overseen'; resellerId: string }

/** The owners who create their own keys: the administrator and a signed-in reseller. */
export type IssuingApiKeyOwner = { kind: 'administrator' } | { kind: 'reseller' }

/** The HTTP surface of the API keys, as {@link ApiKeysApi} exposes it to the store. */
export interface ApiKeysApi {
  /** Reads one page of an owner's keys. */
  list: (owner: ApiKeyOwner, request: ListRequest, signal?: AbortSignal) => Promise<Paged<ApiKey>>
  /** Creates a key for its issuing owner; the answer carries its plaintext. */
  create: (owner: IssuingApiKeyOwner, request: CreateApiKeyRequest, signal?: AbortSignal) => Promise<ApiKeyCreated>
  /** Revokes an owner's key; the answer is an empty 204. */
  revoke: (owner: ApiKeyOwner, id: string, signal?: AbortSignal) => Promise<void>
  /** Lists the scopes the issuing owner may grant a key. */
  scopes: (owner: IssuingApiKeyOwner, signal?: AbortSignal) => Promise<ApiKeyChoice[]>
  /** Lists the states a key can be filtered by. */
  statuses: (owner: ApiKeyOwner, signal?: AbortSignal) => Promise<ApiKeyChoice[]>
}
