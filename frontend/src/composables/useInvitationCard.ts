import { computed, onBeforeUnmount, onMounted, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useInvitationsStore } from '../stores/invitations'
import { useLocaleStore } from '../stores/locale'
import type { InvitationState, InvitationSubject } from '../types/invitation'
import { absoluteInvitationLink } from '../utils/absoluteInvitationLink'
import { formatDate } from '../utils/formatDate'

/** What {@link useInvitationCard} hands to an invitation card. */
export interface InvitationCardState {
  /** The invitations store the card renders (status, link, loading, acting, resent, errorMessage). */
  store: ReturnType<typeof useInvitationsStore>
  /** Whether the link on screen has been copied. */
  copied: Ref<boolean>
  /** The link to show, made absolute against the origin the operator is looking at; empty when none. */
  displayedLink: ComputedRef<string>
  /** When the link on screen stops working, formatted for the panel's current language. */
  linkExpiry: ComputedRef<string>
  /** The sentence describing where the invitation stands; empty before it is known. */
  stateText: ComputedRef<string>
  /** Mints a one-time link and puts it on screen, replacing any earlier one. */
  createLink: () => Promise<void>
  /** Mails the subject's contact a fresh invitation. */
  resend: () => Promise<void>
  /** Copies the link on screen to the clipboard; a failure leaves `copied` false. */
  copyLink: () => Promise<void>
}

/**
 * The behaviour both invitation cards share, the account owner's and the reseller's: loading the
 * invitation's state, minting a link, resending the mail, copying the link and the life of the link in
 * memory. The cards keep their own markup and wording; they differ only in the subject they address
 * and the sentences that describe each state, which they translate themselves so every key stays
 * spelled out in the card that owns it (the locale check can then see it is used).
 *
 * The link lives in the invitations store's memory only. It is dropped when the operator leaves
 * (`onBeforeUnmount`), changes subject (`watch`) or resends the e-mail, and it is never written to
 * browser storage, put in a URL or logged. A failed copy is swallowed on purpose: the link is also
 * rendered as selectable text, and the honest answer to a blocked clipboard is the unchanged button.
 *
 * Must be called during a component's setup, because it registers lifecycle hooks.
 * @param subject The invitation the card addresses; a different id starts from a clean slate.
 * @param describeState Words one state for the card: `date` is the formatted expiry, meaningful only
 *   while the state is `pending`.
 * @returns The state and actions the card renders.
 */
export const useInvitationCard = (
  subject: ComputedRef<InvitationSubject>,
  describeState: (state: InvitationState, date: string) => string,
): InvitationCardState => {
  const store = useInvitationsStore()
  const localeStore = useLocaleStore()

  /**
   * Whether the link on screen has been copied. Reported because a copy button that looks the same
   * before and after a click invites a second click.
   */
  const copied: Ref<boolean> = ref(false)

  /** The link to show: the panel's own, made absolute against the origin the operator is looking at. */
  const displayedLink: ComputedRef<string> = computed(() => {
    return store.link === null ? '' : absoluteInvitationLink(store.link.link, window.location.origin)
  })

  /** When the link on screen stops working, formatted for the panel's current language. */
  const linkExpiry: ComputedRef<string> = computed(() => {
    return store.link === null ? '' : formatDate(store.link.expiresAt, localeStore.current)
  })

  /** The sentence describing where the invitation stands, or an empty string before it is known. */
  const stateText: ComputedRef<string> = computed(() => {
    const status = store.status
    if (status === null) {
      return ''
    }

    return describeState(
      status.state,
      status.state === 'pending' ? formatDate(status.expiresAt, localeStore.current) : '',
    )
  })

  /**
   * Loads the invitation's state for the subject the card is showing.
   * @returns Resolves once the request has settled.
   */
  const load = async (): Promise<void> => {
    copied.value = false
    await store.loadStatus(subject.value)
  }

  /**
   * Creates a one-time link and puts it on screen. Asked again, it replaces the link on screen with
   * a newer one and the older stops working.
   * @returns Resolves once the request has settled.
   */
  const createLink = async (): Promise<void> => {
    copied.value = false
    await store.issueLink(subject.value)
  }

  /**
   * Mails the subject's contact a fresh invitation.
   * @returns Resolves once the request has settled.
   */
  const resend = async (): Promise<void> => {
    copied.value = false
    await store.resend(subject.value)
  }

  /**
   * Copies the link to the clipboard. Reached only from a click on the copy button; a failure is
   * swallowed on purpose and leaves `copied` false, beside a link the operator can still select.
   * @returns Resolves once the clipboard write has settled, successfully or not.
   */
  const copyLink = async (): Promise<void> => {
    try {
      await navigator.clipboard.writeText(displayedLink.value)
      copied.value = true
    } catch {
      copied.value = false
    }
  }

  onMounted(load)

  // A different subject under the same card must start from a clean slate, not from the last
  // subject's link.
  watch(
    (): string => {
      return subject.value.id
    },
    load,
  )

  // Leaving the page is the end of the link's usefulness; the store forgets it so it cannot be found
  // again by returning to the screen.
  onBeforeUnmount(store.forget)

  return { store, copied, displayedLink, linkExpiry, stateText, createLink, resend, copyLink }
}
