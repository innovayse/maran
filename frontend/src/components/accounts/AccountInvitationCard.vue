<script setup lang="ts">
/**
 * The invitation of one hosting account's owner, on the account's own page: where it stands, and
 * the two ways to get it to them — a one-time link the administrator hands over themselves, or a
 * fresh e-mail.
 *
 * **Why a link is on offer at all (#78).** An administrator never sets a customer's password, so
 * the only way in for a new customer is the invitation. A fresh install has no mail server, so the
 * mail that carries it is never sent — and the panel keeps only a digest of the token, which cannot
 * be turned back into a link. Creating a link mints a NEW token and shows it here, once; it never
 * recovers an old one. The card says so before the button is pressed: creating a link replaces any
 * invitation already sent, and the earlier link stops working. Nothing is created until the
 * operator clicks, so merely opening the page never retires the link that was mailed.
 *
 * **A dead link is never offered.** The state is read from the panel every time the page opens, so
 * an accepted invitation shows no link controls at all and an expired one says it has expired. The
 * link itself lives in the invitations store's memory only and is dropped when the operator leaves
 * (`onBeforeUnmount`), changes account (`watch`), or resends the e-mail — which replaces the token.
 * It is never written to browser storage, put in a URL or logged, and is copied only by a click.
 *
 * The link is rendered as selectable text as well as being copyable: a clipboard write can fail
 * silently in a locked-down browser, and a value nobody can select is a value nobody can hand over.
 * A failed copy is swallowed on purpose and leaves the button unchanged rather than claiming the
 * panel did something wrong.
 *
 * **A suspended account is offered nothing (#216).** The panel refuses both actions for it, so the
 * card hides them and says that the account must be reactivated first, rather than offering
 * buttons that can only answer with a refusal. The server is the boundary; this is the advice.
 *
 * Administrator-only on the server (`InvitationsController`); this card adds no matching role
 * check of its own (rules/architecture.md — the endpoint is the boundary).
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import { useInvitationCard } from '../../composables/useInvitationCard'
import type { AccountStatus } from '../../types/account'
import type { InvitationState, InvitationSubject } from '../../types/invitation'

/** Props accepted by {@link AccountInvitationCard}. */
const props = defineProps<{
  /** The hosting account whose owner's invitation this card shows. */
  accountId: string
  /**
   * The account's lifecycle state. The panel issues no invitation for a suspended account
   * (`InvitationAccountSuspended`), so the card offers none and says why instead.
   */
  status: AccountStatus
}>()

const { t } = useI18n()

/** The invitation this card addresses: the owner of the hosting account it was given. */
const subject: ComputedRef<InvitationSubject> = computed(() => {
  return { kind: 'account', id: props.accountId }
})

/**
 * Whether the panel will issue this account an invitation, decided from its lifecycle state so a
 * state added to `AccountStatus` has to be answered here rather than read as "not suspended".
 */
const issuance: ComputedRef<'allowed' | 'refused'> = computed(() => {
  return issuanceOf(props.status)
})

/**
 * Words one invitation state in the panel's language; the pending sentence carries the expiry.
 * @param state Where the invitation stands.
 * @param date The formatted expiry, empty unless the state is `pending`.
 * @returns The sentence for the state.
 */
const describeState = (state: InvitationState, date: string): string => {
  return state === 'pending' ? t('invitations.state.pending', { date }) : t(`invitations.state.${state}`)
}

// Loading, minting, resending, copying and forgetting the link are shared with the reseller card.
const { store, copied, displayedLink, linkExpiry, stateText, createLink, resend, copyLink } = useInvitationCard(
  subject,
  describeState,
)

/**
 * Says whether an account in the given lifecycle state may be issued an invitation.
 *
 * A function rather than a switch inside the computed so a state added to `AccountStatus` is a
 * compile error here and not a silent `undefined`.
 * @param status The account's lifecycle state.
 * @returns `allowed` for an active account, `refused` for a suspended one.
 */
const issuanceOf = (status: AccountStatus): 'allowed' | 'refused' => {
  switch (status) {
    case 'active':
      return 'allowed'
    case 'suspended':
      return 'refused'
  }
}
</script>

<template>
  <UiCard class="flex flex-col gap-4" data-testid="invitation-card">
    <UiSectionHeading :title="t('invitations.heading')" :subtitle="t('invitations.subtitle')" />

    <UiSpinner v-if="store.loading" :label="t('invitations.loading')" />

    <template v-else-if="store.status !== null">
      <p class="text-sm text-text-primary" data-testid="invitation-state">{{ stateText }}</p>

      <!-- Nothing more is offered once the owner has a password: there is no invitation left to
           hand over, and a link here would be a way to reset a live customer's access. -->
      <template v-if="store.status.state !== 'accepted'">
        <UiAlert v-if="store.errorMessage !== null" variant="error">
          {{ store.errorMessage }}
        </UiAlert>

        <!-- Says SENT, never delivered: the mail queue is deliberately non-durable and this card
             has no way to know whether the mail arrived. -->
        <UiAlert v-if="store.resent" variant="info">
          {{ t('accounts.detail.invitationResent') }}
        </UiAlert>

        <div v-if="store.link !== null" class="flex flex-col gap-2" data-testid="invitation-link-block">
          <p class="text-xs font-medium text-text-muted">
            {{ t('invitations.linkLabel') }}
          </p>
          <!-- Selectable text, not a masked field: the clipboard may be unavailable, and the only
               other way to hand this value over is to read or select it. -->
          <p
            data-testid="invitation-link"
            class="rounded-control border border-border-strong bg-surface-2 px-3 py-2.5 font-mono text-sm wrap-anywhere text-text-primary"
          >
            {{ displayedLink }}
          </p>
          <p class="text-sm text-text-muted">
            {{ t('invitations.linkWarning', { date: linkExpiry }) }}
          </p>
          <div>
            <UiButton variant="secondary" @click="copyLink">
              <UiIcon name="copy" size="sm" />
              {{ copied ? t('common.copied') : t('invitations.copyLink') }}
            </UiButton>
          </div>
        </div>

        <UiAlert v-if="issuance === 'refused'" variant="info" data-testid="invitation-suspended">
          {{ t('invitations.suspendedNote') }}
        </UiAlert>

        <p v-if="issuance === 'allowed'" class="text-sm text-text-muted">{{ t('invitations.replaceNote') }}</p>

        <div v-if="issuance === 'allowed'" class="flex flex-wrap items-center gap-2">
          <UiButton :disabled="store.acting" @click="createLink">
            {{
              store.acting
                ? t('invitations.creating')
                : store.link === null
                  ? t('invitations.createLink')
                  : t('invitations.createAnotherLink')
            }}
          </UiButton>
          <UiButton variant="secondary" :disabled="store.acting" @click="resend">
            {{ t('accounts.detail.resendInvitation') }}
          </UiButton>
        </div>
      </template>
    </template>

    <UiAlert v-else-if="store.errorMessage !== null" variant="error">
      {{ store.errorMessage }}
    </UiAlert>
  </UiCard>
</template>
