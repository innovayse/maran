import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useApiKeysApi } from '../composables/apis/useApiKeysApi'
import { ApiError } from '../composables/useApi'
import type { ApiKey, ApiKeyChoice, ApiKeyOwner, CreateApiKeyRequest, IssuingApiKeyOwner, RevealedApiKey } from '../types/apiKey'
import type { ListLoad, ListRequest } from '../types/list'

/**
 * Owns one page of an owner's API keys (the administrator's own, a signed-in reseller's own, or one
 * reseller's as the administrator oversees them; the {@link ApiKeyOwner} each action takes says which),
 * the choices the create form and the status filter offer,
 * the create and revoke workflows, and the one-time secret a create produces. The API keys page
 * reads state from here and calls its actions; it never touches the API layer (rules/vue.md).
 *
 * Error text is never generated here: the backend's already-localized message is stored verbatim.
 *
 * **The secret is held in memory and nowhere else.** {@link revealedKey} is a plain `ref`, never
 * written to `localStorage`, `sessionStorage`, the URL, a history entry or the console, so a reload
 * loses it, which is the truth: the server kept only a hash. The page clears it when the dialog
 * closes and again on unmount.
 */
export const useApiKeysStore = defineStore('apiKeys', () => {
  const api = useApiKeysApi()

  /** The rows of the page last answered by the panel; empty before the first answer. */
  const keys: Ref<ApiKey[]> = ref([])

  /** The scopes a key may be granted, as the panel named them; empty until read or when unreadable. */
  const scopes: Ref<ApiKeyChoice[]> = ref([])

  /** The states the list can be filtered by, as the panel named them; empty until read or when unreadable. */
  const statuses: Ref<ApiKeyChoice[]> = ref([])

  /** True while a create request is in flight. */
  const creating: Ref<boolean> = ref(false)

  /** True while a revoke request is in flight. */
  const revoking: Ref<boolean> = ref(false)

  /** The panel's message for the last failed create, or `null`; kept apart so the list is not blanked. */
  const createErrorMessage: Ref<string | null> = ref(null)

  /** The panel's message for the last failed revoke, or `null`. */
  const revokeErrorMessage: Ref<string | null> = ref(null)

  /**
   * The key the panel has just produced, or `null` when there is none to show. Set only by
   * {@link create} and cleared by {@link dismissKey}.
   */
  const revealedKey: Ref<RevealedApiKey | null> = ref(null)

  /**
   * Loads one page of keys into {@link keys} and reports how the read ended, for the page's
   * `useServerTable`.
   * @param owner Whose keys to read.
   * @param request The page, size, search, sort and status filter, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (owner: ApiKeyOwner, request: ListRequest): Promise<ListLoad<ApiKey>> => {
    try {
      const page = await api.list(owner, request)
      keys.value = page.items
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Reads the grantable scopes into {@link scopes}. A failure leaves the form without choices; the
   * server refuses what it does not know either way.
   * @param owner The issuing owner: each is offered its own set.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadScopes = async (owner: IssuingApiKeyOwner): Promise<void> => {
    try {
      scopes.value = await api.scopes(owner)
    } catch {
      // The choices are an aid to the form, not the list: an unreadable set leaves it empty.
      scopes.value = []
    }
  }

  /**
   * Reads the filterable states into {@link statuses}. The names are language-dependent, so the
   * page asks again when the interface language changes.
   * @param owner Whose keys the filter is for.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadStatuses = async (owner: ApiKeyOwner): Promise<void> => {
    try {
      statuses.value = await api.statuses(owner)
    } catch {
      // The filter is an aid, not the list: an unreadable set leaves the filter without choices.
      statuses.value = []
    }
  }

  /**
   * Names a scope as the panel named it.
   * @param value The scope as the wire spells it.
   * @returns The panel's name, or the value itself while the names have not arrived.
   */
  const scopeName = (value: string): string => {
    return (
      scopes.value.find((choice) => {
        return choice.value === value
      })?.name ?? value
    )
  }

  /**
   * Names a state as the panel named it.
   * @param value The state as the wire spells it.
   * @returns The panel's name, or the value itself while the names have not arrived.
   */
  const statusName = (value: string): string => {
    return (
      statuses.value.find((choice) => {
        return choice.value === value
      })?.name ?? value
    )
  }

  /**
   * Whether a `?status=` value is worth sending.
   * @param value The value from the URL.
   * @returns True while the set has not arrived (the server decides), otherwise whether it was listed.
   */
  const acceptsStatus = (value: string): boolean => {
    return (
      statuses.value.length === 0 ||
      statuses.value.some((choice) => {
        return choice.value === value
      })
    )
  }

  /**
   * Forgets the secret on screen. Called when the dialog closes and when the page unmounts, so
   * leaving the screen ends the one chance to read it.
   * @returns Nothing.
   */
  const dismissKey = (): void => {
    revealedKey.value = null
  }

  /**
   * Creates a key and holds its secret for exactly one showing. The held page is not patched: where
   * the new row falls is the server's sort to say, so the page reads again.
   * @param owner The issuing owner; the panel takes the owner from the token, never from the body.
   * @param request The name, scopes, allowed ranges and expiry.
   * @returns True when the panel created the key; otherwise {@link createErrorMessage} says why.
   */
  const create = async (owner: IssuingApiKeyOwner, request: CreateApiKeyRequest): Promise<boolean> => {
    creating.value = true
    try {
      const created = await api.create(owner, request)
      // Only the name and the secret are kept: the row itself comes back through the list.
      revealedKey.value = { name: created.key.name, secret: created.secret }
      createErrorMessage.value = null
      return true
    } catch (error) {
      createErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      creating.value = false
    }
  }

  /**
   * Revokes a key. The page then reads again, because the key's state changed on the server.
   * @param owner Whose key it is.
   * @param id The key to revoke.
   * @returns True when the panel revoked it.
   */
  const revoke = async (owner: ApiKeyOwner, id: string): Promise<boolean> => {
    revoking.value = true
    try {
      await api.revoke(owner, id)
      revokeErrorMessage.value = null
      return true
    } catch (error) {
      revokeErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      revoking.value = false
    }
  }

  /**
   * Clears the last create failure, so a reopened dialog starts without it.
   * @returns Nothing.
   */
  const clearCreateError = (): void => {
    createErrorMessage.value = null
  }

  return {
    keys,
    scopes,
    statuses,
    creating,
    revoking,
    createErrorMessage,
    revokeErrorMessage,
    revealedKey,
    list,
    loadScopes,
    loadStatuses,
    scopeName,
    statusName,
    acceptsStatus,
    dismissKey,
    create,
    revoke,
    clearCreateError,
  }
})
