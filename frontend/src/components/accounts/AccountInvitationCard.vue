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
 * Administrator-only on the server (`InvitationsController`); this card adds no matching role
 * check of its own (rules/architecture.md — the endpoint is the boundary).
 */
import { computed, onBeforeUnmount, onMounted, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import { useInvitationsStore } from '../../stores/invitations'
import { useLocaleStore } from '../../stores/locale'
import { absoluteInvitationLink } from '../../utils/absoluteInvitationLink'
import { formatDate } from '../../utils/formatDate'

/** Props accepted by {@link AccountInvitationCard}. */
const props = defineProps<{
  /** The hosting account whose owner's invitation this card shows. */
  accountId: string
}>()

const { t } = useI18n()
const store = useInvitationsStore()
const localeStore = useLocaleStore()

/**
 * Whether the link on screen has been copied to the clipboard.
 *
 * Reported because a copy button that looks the same before and after a click invites a second
 * click, and a second click is how somebody walks away believing they copied when the first
 * attempt threw.
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

  if (status.state === 'pending' && status.expiresAt !== null) {
    return t('invitations.state.pending', { date: formatDate(status.expiresAt, localeStore.current) })
  }

  return t(`invitations.state.${status.state}`)
})

/**
 * Loads the invitation's state for the account the card is showing.
 * @returns Resolves once the request has settled.
 */
const load = async (): Promise<void> => {
  copied.value = false
  await store.loadStatus(props.accountId)
}

/**
 * Creates a one-time link and puts it on screen. Asked again, it replaces the link on screen
 * with a newer one and the older stops working.
 * @returns Resolves once the request has settled.
 */
const createLink = async (): Promise<void> => {
  copied.value = false
  await store.issueLink(props.accountId)
}

/**
 * Mails the owner a fresh invitation.
 * @returns Resolves once the request has settled.
 */
const resend = async (): Promise<void> => {
  copied.value = false
  await store.resend(props.accountId)
}

/**
 * Copies the link to the clipboard. Reached only from a click on the copy button.
 *
 * A failure is swallowed on purpose and leaves {@link copied} false: the clipboard is blocked
 * outright in some browser configurations, and the honest answer there is the unchanged button
 * beside a link the operator can still select by hand.
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

// A different account under the same card must start from a clean slate, not from the last
// account's link.
watch(
  (): string => {
    return props.accountId
  },
  load,
)

// Leaving the page is the end of the link's usefulness; the store forgets it so it cannot be found
// again by returning to the screen.
onBeforeUnmount(store.forget)
</script>

<template>
  <UiCard class="flex flex-col gap-4" data-testid="invitation-card">
    <UiSectionHeading :title="t('invitations.heading')" :subtitle="t('invitations.subtitle')" />

    <UiSpinner v-if="store.loading" :label="t('invitations.loading')" />

    <template v-else-if="store.status !== null">
      <p class="text-base text-text-primary" data-testid="invitation-state">{{ stateText }}</p>

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
          <p class="text-xs font-medium tracking-wide text-text-muted uppercase">
            {{ t('invitations.linkLabel') }}
          </p>
          <!-- Selectable text, not a masked field: the clipboard may be unavailable, and the only
               other way to hand this value over is to read or select it. -->
          <p
            data-testid="invitation-link"
            class="rounded-control border border-border-strong bg-surface-2 px-3 py-2.5 font-mono text-base wrap-anywhere text-text-primary"
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

        <p class="text-sm text-text-muted">{{ t('invitations.replaceNote') }}</p>

        <div class="flex flex-wrap items-center gap-2">
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
