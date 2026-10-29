import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useInvitationsApi } from '../composables/apis/useInvitationsApi'
import { ApiError } from '../composables/useApi'
import type { InvitationLink, InvitationStatus } from '../types/invitation'

/**
 * Owns what the account page knows about an account owner's invitation: where it stands, and the
 * one-time link while the operator is looking at it. The card that renders it reads state from here
 * and calls its actions — it never touches the API composable (rules/vue.md: "API composables are
 * called from Pinia stores ONLY").
 *
 * **The link is a credential and is held here, in memory, for as long as it is useful and no
 * longer.** The panel stores only a digest of the token inside it, so this is the sole copy: it is
 * never written to browser storage, never put in a URL or a route, and is dropped by
 * {@link forget} when the operator leaves the page, by {@link loadStatus} when another account (or
 * a refreshed state) is loaded, and by {@link resend}, which replaces the token and so kills it.
 * Keeping it past those points would be offering a link that no longer works.
 *
 * Error text is never generated here: when the backend rejects a request, its already-localized
 * `title`/`detail` is stored verbatim (rules/vue.md: "the backend owns their text").
 */
export const useInvitationsStore = defineStore('invitations', () => {
  const api = useInvitationsApi()

  /** Where the invitation stands as last reported, or `null` before it is loaded. */
  const status: Ref<InvitationStatus | null> = ref(null)

  /**
   * The one-time link the operator asked for, or `null` when none is on screen. A credential:
   * memory only, see the store's remarks.
   */
  const link: Ref<InvitationLink | null> = ref(null)

  /** True while the status request is in flight. */
  const loading: Ref<boolean> = ref(false)

  /** True while a link is being created or an invitation resent. */
  const acting: Ref<boolean> = ref(false)

  /** True once the panel accepted a resend and no later action has replaced that fact. */
  const resent: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the most recent failed request, or `null` when the last one
   * succeeded or none has been made. Rendered verbatim by the card.
   */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Drops everything held, the link above all. Called when the operator leaves the account's page.
   * @returns Nothing.
   */
  const forget = (): void => {
    status.value = null
    link.value = null
    resent.value = false
    errorMessage.value = null
  }

  /**
   * Loads where an account owner's invitation stands, first forgetting anything held — a link
   * shown for one account must never sit under another's heading, nor outlive a state change.
   * @param accountId The hosting account being asked about.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadStatus = async (accountId: string): Promise<void> => {
    forget()
    loading.value = true
    try {
      status.value = await api.getStatus(accountId)
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
    } finally {
      loading.value = false
    }
  }

  /**
   * Creates a one-time invitation link and holds it for the card to show. This retires every
   * invitation already sent, so the status becomes `pending` with the new expiry.
   * @param accountId The hosting account whose owner is being invited.
   * @returns True when the panel issued a link.
   */
  const issueLink = async (accountId: string): Promise<boolean> => {
    acting.value = true
    errorMessage.value = null
    resent.value = false
    try {
      const issued = await api.issueLink(accountId)
      link.value = issued
      status.value = { state: 'pending', expiresAt: issued.expiresAt }
      return true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Mails the account's owner a fresh invitation. The token it replaces is spent, so any link held
   * here is dead the moment this succeeds and is dropped.
   *
   * The panel's mail queue is deliberately non-durable, so a `true` here means the invitation was
   * SENT, never that it was delivered — the caller renders that distinction, not this action.
   * @param accountId The hosting account whose owner is being (re)invited.
   * @returns True once a new invitation was sent.
   */
  const resend = async (accountId: string): Promise<boolean> => {
    acting.value = true
    errorMessage.value = null
    resent.value = false
    try {
      await api.resend(accountId)
      link.value = null
      resent.value = true
      status.value = await api.getStatus(accountId)
      return true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  return { status, link, loading, acting, resent, errorMessage, loadStatus, issueLink, resend, forget }
})
