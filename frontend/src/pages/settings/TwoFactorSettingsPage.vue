<script setup lang="ts">
/**
 * Turning the second factor on and off. Renders a `<section>`, not a `<main>` —
 * the single `<main>` landmark lives in the layout this page is nested under.
 *
 * Enrolment is two steps because the backend makes it two: the secret is handed
 * over first and enables nothing, and only a code produced from it turns the
 * factor on. Somebody who adds the secret to a dead app, or closes the tab
 * halfway, is therefore not locked out of their own panel.
 *
 * Laid out as the redesign's screens are (docs/design/redesign-2026-10): the heading block with
 * its eyebrow, then panels. Rebuilt from zero, with the behaviour unchanged except for the one
 * thing the redesign brief asks for on purpose:
 *
 * - **One flow per state** ({@link TwoFactorState}): Set up when the factor is off, the secret
 *   with its QR code and the confirm form while enrolling, and "on" with Turn off when it is on.
 *   The starting state comes from the profile, or — on the steered route, whose token cannot read
 *   the profile — from the session's `requiresTwoFactorSetup`. A successful confirm or disable is
 *   itself the new state, so neither is followed by a re-read.
 * - Machine text is set in the mono face and in its own case: the secret and every recovery code.
 *   Both are shown exactly as the server returned them, once, and never re-derived or stored — no
 *   ref outside this component holds them, and nothing writes them anywhere.
 * - Recovery codes come first when they exist, under a warning: they are the one thing on this
 *   screen the person must act on before leaving, because the backend keeps only hashes and they
 *   cannot be shown a second time.
 * - Turning the factor off is destructive — the account is then guarded by a password alone — so
 *   it confirms first, in a dialog that names the consequence. The typed code is submitted only
 *   after the answer, and the dialog stays on screen over a spinner while the request is with the
 *   server. It closes either way: a refusal is rendered by the page underneath, in the alert.
 * - The code for turning off and the code for confirming an enrolment are separate fields with
 *   separate state, so a half-typed code in one can never be submitted by the other.
 * - **The title's level depends on where the page is shown.** The same page serves two routes:
 *   `/settings/two-factor` inside the shell, where the page's own title is the document's single
 *   `h1` ({@link UiPageHeading}), and `/two-factor-setup` inside the auth layout, whose wordmark is
 *   already the screen's `h1`. A second `h1` there would give the screen two top-level headings,
 *   so under the auth layout the title is the `h2` of {@link UiAuthHeading}, like every other
 *   signed-out screen. The page decides by its own route (it is the one that knows which of the
 *   two it was mounted for) rather than taking a prop, because the router mounts it without any.
 * - Nothing here scrolls sideways at 390 px: the secret breaks anywhere, the recovery codes wrap
 *   into one column on a phone, and the buttons wrap beneath each other.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiAuthHeading from '../../components/ui/UiAuthHeading.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiIconTile from '../../components/ui/UiIconTile.vue'
import UiInput from '../../components/ui/UiInput.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import type { LoadingState } from '../../types/loading'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiQrCode from '../../components/ui/UiQrCode.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiSkeleton from '../../components/ui/UiSkeleton.vue'
import { useAuthStore } from '../../stores/auth'
import { useProfileStore } from '../../stores/profile'
import type { TotpEnrolment } from '../../types/auth'

/**
 * Where the caller's second factor stands, and therefore which ONE flow the page renders.
 *
 * A discriminated union rather than a name plus loose refs, so the data a state needs travels
 * inside it and cannot disagree with it: there is no "enrolling" without a secret, and no
 * recovery codes outside "enrolled".
 *
 * - `loading`: the profile is being read; nothing is offered yet.
 * - `unavailable`: the profile could not be read. The page offers nothing rather than guess —
 *   guessing is how it used to offer Set up and Turn off at once — and the store's message says why.
 * - `notEnrolled`: no second factor; the page offers Set up.
 * - `enrolling`: a secret has been handed out and not yet confirmed.
 * - `enrolled`: the factor is on; the page says so and offers Turn off. `recoveryCodes` holds the
 *   codes only right after a confirmation in this tab — the backend keeps hashes, so they can never
 *   be shown again — and is `null` otherwise.
 */
type TwoFactorState =
  | { kind: 'loading' }
  | { kind: 'unavailable' }
  | { kind: 'notEnrolled' }
  | { kind: 'enrolling'; enrolment: TotpEnrolment }
  | { kind: 'enrolled'; recoveryCodes: string[] | null }

const { t } = useI18n()
const authStore = useAuthStore()
const profileStore = useProfileStore()
const route = useRoute()

/** The caller's two-factor state; see {@link TwoFactorState}. */
const state: Ref<TwoFactorState> = ref({ kind: 'loading' })

/** The code being typed to confirm an enrolment. */
const enrolCode: Ref<string> = ref('')

/** The code being typed to turn the factor off. */
const disableCode: Ref<string> = ref('')

/** Whether the user has asked to turn the factor off and is being asked to confirm. */
const confirmingDisable: Ref<boolean> = ref(false)

/** Whether the confirmed request to turn the factor off is with the server. */
const disabling: Ref<boolean> = ref(false)

/**
 * Whether the page is shown as the steered-enrolment screen, i.e. inside the auth layout. Read
 * from the route name because that route is the one place the router mounts this page outside the
 * shell; see the component note for why the title's heading level follows it.
 */
const inAuthLayout: ComputedRef<boolean> = computed(() => {
  return route.name === 'two-factor-setup'
})

/**
 * Where the page's starting state stands, for the loading region: still settling, settled into a
 * flow, or `unavailable` (the profile would not load), which the region shows as its error.
 */
const regionState: ComputedRef<LoadingState> = computed(() => {
  switch (state.value.kind) {
    case 'loading':
      return 'loading'
    case 'unavailable':
      return 'error'
    default:
      return 'loaded'
  }
})

/**
 * Settles the starting state.
 *
 * On the steered route the answer is already known and the profile is not asked: that token can
 * reach nothing but enrolment (the profile endpoint answers 403), and being steered there at all
 * means the caller has no second factor yet. Everywhere else the profile is the one source.
 * @returns Resolves once the state is settled.
 */
const loadState = async (): Promise<void> => {
  if (authStore.requiresTwoFactorSetup) {
    state.value = { kind: 'notEnrolled' }
    return
  }

  await profileStore.load()
  const profile = profileStore.profile
  if (profile === null) {
    state.value = { kind: 'unavailable' }
    return
  }

  state.value = profile.isTwoFactorEnabled ? { kind: 'enrolled', recoveryCodes: null } : { kind: 'notEnrolled' }
}

/**
 * Starts an enrolment and shows the secret.
 * @returns Resolves once the request has settled.
 */
const beginEnrolment = async (): Promise<void> => {
  const enrolment = await authStore.beginTwoFactorEnrolment()
  if (enrolment !== null) {
    state.value = { kind: 'enrolling', enrolment }
  }
}

/**
 * Abandons an enrolment that was never confirmed. Nothing was enabled, so nothing
 * has to be undone.
 * @returns Nothing; the page returns to Set up.
 */
const cancelEnrolment = (): void => {
  state.value = { kind: 'notEnrolled' }
  enrolCode.value = ''
}

/**
 * Confirms the enrolment and shows the recovery codes, once. A successful answer IS the new
 * state — the factor is on — so nothing is read back.
 * @returns Resolves once the request has settled.
 */
const confirmEnrolment = async (): Promise<void> => {
  if (state.value.kind !== 'enrolling') {
    return
  }

  const codes = await authStore.confirmTwoFactorEnrolment(state.value.enrolment.secret, enrolCode.value)
  if (codes !== null) {
    state.value = { kind: 'enrolled', recoveryCodes: codes.codes }
    enrolCode.value = ''
  }
}

/**
 * Asks the user to confirm turning the factor off. Nothing is sent yet.
 * @returns Nothing; the confirmation dialog opens.
 */
const askToDisable = (): void => {
  confirmingDisable.value = true
}

/**
 * Withdraws the question, unless the answer is already with the server.
 * @returns Nothing; the dialog closes and the typed code stays where it was.
 */
const cancelDisable = (): void => {
  if (!disabling.value) {
    confirmingDisable.value = false
  }
}

/**
 * Turns the second factor off, for a caller who can still satisfy it, once they have confirmed.
 * A successful answer is the new state — no factor — so nothing is read back.
 * @returns Resolves once the request has settled.
 */
const disable = async (): Promise<void> => {
  disabling.value = true
  try {
    if (await authStore.disableTwoFactor(disableCode.value)) {
      disableCode.value = ''
      state.value = { kind: 'notEnrolled' }
    }
  } finally {
    disabling.value = false
    confirmingDisable.value = false
  }
}

/**
 * Copies the recovery codes to the clipboard, one per line.
 * @returns Resolves once the clipboard write has settled.
 */
const copyRecoveryCodes = async (): Promise<void> => {
  if (state.value.kind === 'enrolled' && state.value.recoveryCodes !== null) {
    await navigator.clipboard.writeText(state.value.recoveryCodes.join('\n'))
  }
}

onMounted(async () => {
  await loadState()
})
</script>

<template>
  <section class="flex w-full flex-col gap-4">
    <UiAuthHeading
      v-if="inAuthLayout"
      :title="t('app.twoFactor.heading')"
      :subtitle="t('app.twoFactor.subtitle')"
    />
    <UiPageHeading
      v-else
      :eyebrow="t('app.twoFactor.eyebrow')"
      :title="t('app.twoFactor.heading')"
      :subtitle="t('app.twoFactor.subtitle')"
    />

    <!-- A refused action; a profile that would not load is the region's own error below. -->
    <UiAlert v-if="authStore.errorMessage !== null" variant="error">
      {{ authStore.errorMessage }}
    </UiAlert>

    <UiLoadingRegion :state="regionState">
      <template #error>
        <UiAlert variant="error">{{ profileStore.errorMessage ?? t('app.status.unreachable') }}</UiAlert>
      </template>
      <div class="flex flex-col gap-4">
        <!-- Exactly one flow per state: the page never offers Set up to someone who has the factor on,
             nor Turn off to someone who does not. Which flow is not known until the profile answers,
             so the wait holds a generic card. -->
        <div v-if="state.kind === 'loading'" class="max-w-2xl">
          <UiSkeleton shape="card" />
        </div>

        <UiCard v-else-if="state.kind === 'notEnrolled'" class="flex max-w-2xl flex-col items-start gap-4">
          <div class="flex items-start gap-3">
            <!-- Decorative: the section's title beside it names it. -->
            <UiIconTile name="shieldCheck" size="sm" />
            <UiSectionHeading
              :title="t('app.twoFactor.manageTitle')"
              :subtitle="t('app.twoFactor.manageDescription')"
            />
          </div>
          <UiButton @click="beginEnrolment">{{ t('app.twoFactor.enable') }}</UiButton>
        </UiCard>

        <UiCard v-else-if="state.kind === 'enrolling'" class="flex max-w-2xl flex-col gap-4">
          <UiSectionHeading
            :title="t('app.twoFactor.enrolTitle')"
            :subtitle="t('app.twoFactor.enrolInstructions')"
          />

          <!-- The QR code is encoded in this browser from the URI the backend returned; the URI holds
               the secret, so it is never handed to anything that could send it elsewhere. -->
          <UiQrCode :value="state.enrolment.provisioningUri" :label="t('app.twoFactor.qrLabel')" />

          <!-- The secret is shown as text too, because the person setting this up is often at the
               machine showing the code, where scanning their own screen is not possible.
               Machine text: the mono face, its own case, and it breaks anywhere so a long secret
               cannot widen the page on a phone. -->
          <div class="flex flex-col gap-1.5">
            <p class="text-sm text-text-muted">{{ t('app.twoFactor.secretLabel') }}</p>
            <p
              class="rounded-control border border-border-subtle bg-surface-2 px-3 py-2 font-mono text-sm break-all text-text-primary select-all"
            >
              {{ state.enrolment.secret }}
            </p>
          </div>

          <UiForm @submit="confirmEnrolment">
            <div class="flex flex-col gap-4">
              <UiInput
                v-model="enrolCode"
                mono
                :label="t('app.twoFactor.confirmCodeLabel')"
                :placeholder="t('app.auth.codePlaceholder')"
                required
                autocomplete="one-time-code"
                inputmode="numeric"
              />
              <div class="flex flex-wrap gap-2">
                <UiButton type="submit">{{ t('app.twoFactor.confirmEnrolment') }}</UiButton>
                <UiButton variant="secondary" @click="cancelEnrolment">
                  {{ t('common.cancel') }}
                </UiButton>
              </div>
            </div>
          </UiForm>
        </UiCard>

        <template v-else-if="state.kind === 'enrolled'">
          <!-- The codes come first when they exist: they are the one thing here that cannot be fetched
               again, so the page leads with them and with the warning that says so. -->
          <UiCard v-if="state.recoveryCodes !== null" class="flex max-w-2xl flex-col gap-4">
            <UiSectionHeading :title="t('app.twoFactor.recoveryTitle')" />
            <UiAlert variant="warning">{{ t('app.twoFactor.recoveryWarning') }}</UiAlert>
            <ul class="grid grid-cols-1 gap-2 sm:grid-cols-2">
              <li
                v-for="recoveryCode in state.recoveryCodes"
                :key="recoveryCode"
                class="rounded-control border border-border-subtle bg-surface-2 px-3 py-2 font-mono text-sm break-all text-text-primary tabular-nums"
              >
                {{ recoveryCode }}
              </li>
            </ul>
            <div>
              <UiButton variant="secondary" @click="copyRecoveryCodes">
                <span class="inline-flex items-center gap-2">
                  <UiIcon name="copy" size="sm" />
                  {{ t('app.twoFactor.copyCodes') }}
                </span>
              </UiButton>
            </div>
          </UiCard>

          <UiCard class="flex max-w-2xl flex-col items-start gap-4">
            <div class="flex items-start gap-3">
              <!-- Decorative: the section's title beside it names it. -->
              <UiIconTile name="shieldCheck" size="sm" />
              <UiSectionHeading
                :title="t('app.twoFactor.enabledTitle')"
                :subtitle="t('app.twoFactor.enabledDescription')"
              />
            </div>

            <!-- Not on the steered route: that token reaches enrolment and nothing else, so the disable
                 endpoint would refuse it. The shell's own copy of this page offers it. -->
            <div v-if="!inAuthLayout" class="w-full">
              <UiForm @submit="askToDisable">
                <div class="flex flex-col items-start gap-4">
                  <UiSectionHeading
                    :title="t('app.twoFactor.disableTitle')"
                    :subtitle="t('app.twoFactor.disableDescription')"
                  />
                  <div class="w-full">
                    <UiInput
                      v-model="disableCode"
                      mono
                      :label="t('app.twoFactor.disableCodeLabel')"
                      :placeholder="t('app.auth.codePlaceholder')"
                      required
                      autocomplete="one-time-code"
                      inputmode="numeric"
                    />
                  </div>
                  <UiButton type="submit" variant="destructive">{{ t('app.twoFactor.disable') }}</UiButton>
                </div>
              </UiForm>
            </div>
          </UiCard>
        </template>
      </div>
    </UiLoadingRegion>

    <!-- Outside the branches above on purpose: the panel that opened it is unmounted the moment the
         state changes, and a dialog mounted inside would go with it mid-request. -->
    <UiConfirm
      :open="confirmingDisable"
      :title="t('app.twoFactor.disableConfirmTitle')"
      :question="t('app.twoFactor.disableConfirmQuestion')"
      :confirm-label="t('app.twoFactor.disableConfirm')"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting="disabling"
      :acting-label="t('app.twoFactor.disabling')"
      @close="cancelDisable"
      @confirm="disable"
    />
  </section>
</template>
