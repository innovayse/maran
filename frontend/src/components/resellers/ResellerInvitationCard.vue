<script setup lang="ts">
/**
 * The invitation of one reseller's login, on the reseller's own page: where it stands, and the two
 * ways to get it to the reseller's contact — a fresh e-mail, or a one-time link the administrator
 * hands over themselves when the panel has no mail server.
 *
 * It is the reseller counterpart of the account owner's invitation card and behaves the same way,
 * for the same reasons. The panel keeps only a digest of the token, so a link is never recovered:
 * creating one mints a NEW token and shows it here, once, and the card says before the button is
 * pressed that any invitation already sent stops working. Nothing is created until the operator
 * clicks, so merely opening the page never retires the link that was mailed.
 *
 * **A dead link is never offered.** The state is read from the panel every time the page opens, so
 * an accepted invitation shows no controls at all and an expired one says it has expired. The link
 * lives in the invitations store's memory only and is dropped when the operator leaves
 * (`onBeforeUnmount`), changes reseller (`watch`) or resends the e-mail, which replaces the token.
 * It is never written to browser storage, put in a URL or logged, and is copied only by a click. It
 * is also rendered as selectable text, because a clipboard write can fail silently in a locked-down
 * browser; a failed copy is swallowed on purpose and leaves the button unchanged.
 *
 * **A suspended reseller is offered nothing.** The panel refuses both actions for it, so the card
 * says that the reseller must be reactivated first instead of offering buttons that can only answer
 * with a refusal. The server is the boundary; this is the advice.
 *
 * Administrator-only on the server (`InvitationsController`); this card adds no matching role check
 * of its own (rules/architecture.md — the endpoint is the boundary).
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
import type { InvitationState, InvitationSubject } from '../../types/invitation'
import type { ResellerStatus } from '../../types/reseller'

/** Props accepted by {@link ResellerInvitationCard}. */
const props = defineProps<{
  /** The reseller whose login's invitation this card shows. */
  resellerId: string
  /** The reseller's lifecycle state; the panel issues no invitation for a suspended one. */
  status: ResellerStatus
}>()

const { t } = useI18n()

/** The invitation this card addresses: the login of the reseller it was given. */
const subject: ComputedRef<InvitationSubject> = computed(() => {
  return { kind: 'reseller', id: props.resellerId }
})

/**
 * Words one invitation state in the panel's language; the pending sentence carries the expiry.
 * @param state Where the invitation stands.
 * @param date The formatted expiry, empty unless the state is `pending`.
 * @returns The sentence for the state.
 */
const describeState = (state: InvitationState, date: string): string => {
  return state === 'pending' ? t('resellers.invitation.state.pending', { date }) : t(`resellers.invitation.state.${state}`)
}

// Loading, minting, resending, copying and forgetting the link are shared with the account card.
const { store, copied, displayedLink, linkExpiry, stateText, createLink, resend, copyLink } = useInvitationCard(
  subject,
  describeState,
)
</script>

<template>
  <UiCard class="flex flex-col gap-4" data-testid="reseller-invitation-card">
    <UiSectionHeading :title="t('resellers.invitation.heading')" :subtitle="t('resellers.invitation.subtitle')" />

    <UiSpinner v-if="store.loading" :label="t('invitations.loading')" />

    <template v-else-if="store.status !== null">
      <p class="text-sm text-text-primary" data-testid="reseller-invitation-state">{{ stateText }}</p>

      <!-- Nothing more is offered once the reseller has a password: there is no invitation left to
           hand over, and a link here would be a way to reset a live login's access. -->
      <template v-if="store.status.state !== 'accepted'">
        <UiAlert v-if="store.errorMessage !== null" variant="error">
          {{ store.errorMessage }}
        </UiAlert>

        <!-- Says SENT, never delivered: the mail queue is deliberately non-durable. -->
        <UiAlert v-if="store.resent" variant="info">
          {{ t('resellers.invitation.resent') }}
        </UiAlert>

        <div v-if="store.link !== null" class="flex flex-col gap-2" data-testid="reseller-invitation-link-block">
          <p class="text-xs font-medium text-text-muted">
            {{ t('invitations.linkLabel') }}
          </p>
          <!-- Selectable text, not a masked field: the clipboard may be unavailable. -->
          <p
            data-testid="reseller-invitation-link"
            class="rounded-control border border-border-strong bg-surface-2 px-3 py-2.5 font-mono text-sm wrap-anywhere text-text-primary"
          >
            {{ displayedLink }}
          </p>
          <p class="text-sm text-text-muted">
            {{ t('resellers.invitation.linkWarning', { date: linkExpiry }) }}
          </p>
          <div>
            <UiButton variant="secondary" @click="copyLink">
              <UiIcon name="copy" size="sm" />
              {{ copied ? t('common.copied') : t('invitations.copyLink') }}
            </UiButton>
          </div>
        </div>

        <UiAlert v-if="status === 'suspended'" variant="info" data-testid="reseller-invitation-suspended">
          {{ t('resellers.invitation.suspendedNote') }}
        </UiAlert>

        <p v-if="status === 'active'" class="text-sm text-text-muted">{{ t('resellers.invitation.replaceNote') }}</p>

        <div v-if="status === 'active'" class="flex flex-wrap items-center gap-2">
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
            {{ t('resellers.invitation.resend') }}
          </UiButton>
        </div>
      </template>
    </template>

    <UiAlert v-else-if="store.errorMessage !== null" variant="error">
      {{ store.errorMessage }}
    </UiAlert>
  </UiCard>
</template>
