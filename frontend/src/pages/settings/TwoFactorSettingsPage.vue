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
import { computed, ref, type ComputedRef, type Ref } from 'vue'
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
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import { useAuthStore } from '../../stores/auth'
import type { TotpEnrolment } from '../../types/auth'

const { t } = useI18n()
const authStore = useAuthStore()
const route = useRoute()

/**
 * Whether the page is shown as the steered-enrolment screen, i.e. inside the auth layout. Read
 * from the route name because that route is the one place the router mounts this page outside the
 * shell; see the component note for why the title's heading level follows it.
 */
const inAuthLayout: ComputedRef<boolean> = computed(() => {
  return route.name === 'two-factor-setup'
})

/** The enrolment in progress: a secret the user has been given but not yet confirmed. */
const enrolment: Ref<TotpEnrolment | null> = ref(null)

/** The code being typed to confirm an enrolment. */
const enrolCode: Ref<string> = ref('')

/** The code being typed to turn the factor off. */
const disableCode: Ref<string> = ref('')

/**
 * The recovery codes, held only for as long as this page is open.
 *
 * The backend stores hashes, so these cannot be shown again — not by us and not
 * by anyone reading the database. The screen says so, and this ref is why it is
 * true: nothing writes them anywhere.
 */
const recoveryCodes: Ref<string[] | null> = ref(null)

/** Whether the user has asked to turn the factor off and is being asked to confirm. */
const confirmingDisable: Ref<boolean> = ref(false)

/** Whether the confirmed request to turn the factor off is with the server. */
const disabling: Ref<boolean> = ref(false)

/** True once the user has an enrolment waiting to be confirmed. */
const isEnrolling: ComputedRef<boolean> = computed(() => {
  return enrolment.value !== null
})

/**
 * Starts an enrolment and shows the secret.
 * @returns Resolves once the request has settled.
 */
const beginEnrolment = async (): Promise<void> => {
  recoveryCodes.value = null
  enrolment.value = await authStore.beginTwoFactorEnrolment()
}

/**
 * Abandons an enrolment that was never confirmed. Nothing was enabled, so nothing
 * has to be undone.
 * @returns Nothing; the page returns to its starting state.
 */
const cancelEnrolment = (): void => {
  enrolment.value = null
  enrolCode.value = ''
}

/**
 * Confirms the enrolment and shows the recovery codes, once.
 * @returns Resolves once the request has settled.
 */
const confirmEnrolment = async (): Promise<void> => {
  if (enrolment.value === null) {
    return
  }

  const codes = await authStore.confirmTwoFactorEnrolment(enrolment.value.secret, enrolCode.value)
  if (codes !== null) {
    recoveryCodes.value = codes.codes
    enrolment.value = null
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
 * @returns Resolves once the request has settled.
 */
const disable = async (): Promise<void> => {
  disabling.value = true
  try {
    if (await authStore.disableTwoFactor(disableCode.value)) {
      disableCode.value = ''
      recoveryCodes.value = null
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
  if (recoveryCodes.value !== null) {
    await navigator.clipboard.writeText(recoveryCodes.value.join('\n'))
  }
}
</script>

<template>
  <!-- `@container`: the two panels below choose their columns from THIS section's width, not the
       viewport's. The page is shown in two places of very different width — the shell's wide
       content area, and the narrow card column of the auth layout when an administrator is steered
       into enrolment — and at a 1440px viewport the latter is ~450px wide, so a viewport
       breakpoint squeezed each panel to ~180px and broke "AUTHENTICATOR" mid-word. -->
  <section class="@container flex w-full flex-col gap-4">
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

    <UiAlert v-if="authStore.errorMessage !== null" variant="error">
      {{ authStore.errorMessage }}
    </UiAlert>

    <!-- The codes come first when they exist: they are the one thing here that cannot be fetched
         again, so the page leads with them and with the warning that says so. -->
    <UiCard v-if="recoveryCodes !== null" class="flex max-w-2xl flex-col gap-4">
      <UiSectionHeading :title="t('app.twoFactor.recoveryTitle')" />
      <UiAlert variant="warning">{{ t('app.twoFactor.recoveryWarning') }}</UiAlert>
      <ul class="grid grid-cols-1 gap-2 sm:grid-cols-2">
        <li
          v-for="recoveryCode in recoveryCodes"
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

    <UiCard v-if="isEnrolling && enrolment !== null" class="flex max-w-2xl flex-col gap-4">
      <UiSectionHeading
        :title="t('app.twoFactor.enrolTitle')"
        :subtitle="t('app.twoFactor.enrolInstructions')"
      />

      <!-- The secret is shown as text, because the person setting this up is usually sitting at
           the machine showing the code and photographing their own screen is not always possible.
           Machine text: the mono face, its own case, and it breaks anywhere so a long secret
           cannot widen the page on a phone. -->
      <div class="flex flex-col gap-1.5">
        <p class="text-sm text-text-muted">{{ t('app.twoFactor.secretLabel') }}</p>
        <p
          class="rounded-control border border-border-subtle bg-surface-2 px-3 py-2 font-mono text-sm break-all text-text-primary select-all"
        >
          {{ enrolment.secret }}
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

    <!-- Two columns only once each panel can have 20rem (half of `@2xl`, minus the gap): a section
         title is uppercase display type that must stay on whole words. -->
    <div v-else class="grid gap-4 @2xl:grid-cols-2">
      <UiCard class="flex flex-col items-start gap-4">
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

      <UiCard>
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
      </UiCard>
    </div>

    <!-- Outside the branches above on purpose: the panel that opened it is unmounted when the
         enrolment branch takes over, and a dialog mounted inside would go with it mid-request. -->
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
