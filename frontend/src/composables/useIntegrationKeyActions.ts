import { ref, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useApiKeysStore } from '../stores/apiKeys'
import type { ApiKey, ApiKeyOwner, CreateApiKeyRequest } from '../types/apiKey'

/** What {@link useIntegrationKeyActions} hands the API keys page. */
export interface ApiKeyActions {
  /** Whether the create dialog is open. */
  createOpen: Ref<boolean>
  /** The key awaiting a revoke confirmation, or `null` when none is. */
  pending: Ref<ApiKey | null>
  /** The success toast's text, or `null` while none is showing. */
  notice: Ref<string | null>
  /** Opens the create dialog with the last create failure forgotten. */
  openCreate: () => void
  /** Closes the create dialog without creating. */
  closeCreate: () => void
  /** Creates a key and, on success, closes the form and reads the list again. */
  create: (request: CreateApiKeyRequest) => Promise<void>
  /** Starts a revoke, which then waits for confirmation. */
  askRevoke: (key: ApiKey) => void
  /** Abandons a pending revoke. */
  cancelRevoke: () => void
  /** Carries out the confirmed revoke and reads the list again. */
  confirmRevoke: () => Promise<void>
  /** Hides the success toast. */
  dismissNotice: () => void
}

/**
 * The API keys page's create and revoke workflows: which dialog is open, which key waits for
 * confirmation, and the notice a finished revoke leaves. Requests go through the store; after each
 * one that changed the list the page's own reload runs, because where a row falls is the server's
 * sort to say.
 * @param owner Whose keys the page manages, read at each action because the page's role can settle after setup.
 * @param reload Reads the current list page again.
 * @returns The workflow state and its actions.
 */
export const useIntegrationKeyActions = (owner: () => ApiKeyOwner, reload: () => Promise<void>): ApiKeyActions => {
  const { t } = useI18n()
  const store = useApiKeysStore()

  /** Whether the create dialog is open. */
  const createOpen: Ref<boolean> = ref(false)

  /** The key awaiting a revoke confirmation, or `null`. */
  const pending: Ref<ApiKey | null> = ref(null)

  /** The success toast's text, or `null`. */
  const notice: Ref<string | null> = ref(null)

  /**
   * Opens the create dialog.
   * @returns Nothing.
   */
  const openCreate = (): void => {
    store.clearCreateError()
    createOpen.value = true
  }

  /**
   * Closes the create dialog without creating.
   * @returns Nothing.
   */
  const closeCreate = (): void => {
    createOpen.value = false
  }

  /**
   * Creates a key; on success the form closes and the secret dialog opens (the store holds the
   * secret), then the list is read again.
   * @param request What the dialog collected.
   * @returns Resolves once the create and the re-read have settled.
   */
  const create = async (request: CreateApiKeyRequest): Promise<void> => {
    const issuing = owner()
    if (issuing.kind === 'overseen') {
      // An administrator overseeing a reseller lists and revokes; the page draws no create button,
      // and the panel has no endpoint that would mint a key for a reseller.
      return
    }
    if (await store.create(issuing, request)) {
      createOpen.value = false
      await reload()
    }
  }

  /**
   * Starts a revoke, which then waits for confirmation.
   * @param key The row the administrator acted on.
   * @returns Nothing.
   */
  const askRevoke = (key: ApiKey): void => {
    pending.value = key
  }

  /**
   * Abandons a pending revoke.
   * @returns Nothing.
   */
  const cancelRevoke = (): void => {
    pending.value = null
  }

  /**
   * Carries out the confirmed revoke, then reads the list again: the key's state changed.
   * @returns Resolves once the request has settled.
   */
  const confirmRevoke = async (): Promise<void> => {
    const key = pending.value
    if (key === null) {
      return
    }
    if (await store.revoke(owner(), key.id)) {
      notice.value = t('apiKeys.revoked', { name: key.name })
      await reload()
    }
    // Closed after the request settles rather than before it is sent: the dialog holds the spinner.
    pending.value = null
  }

  /**
   * Hides the success toast.
   * @returns Nothing.
   */
  const dismissNotice = (): void => {
    notice.value = null
  }

  return { createOpen, pending, notice, openCreate, closeCreate, create, askRevoke, cancelRevoke, confirmRevoke, dismissNotice }
}
