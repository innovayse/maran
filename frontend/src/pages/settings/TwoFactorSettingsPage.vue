<script setup lang="ts">
/**
 * Turning the second factor on and off. Renders a `<section>`, not a `<main>` —
 * the single `<main>` landmark lives in the layout this page is nested under.
 *
 * Enrolment is two steps because the backend makes it two: the secret is handed
 * over first and enables nothing, and only a code produced from it turns the
 * factor on. Somebody who scans the QR into a dead app, or closes the tab
 * halfway, is therefore not locked out of their own panel.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiInput from '../../components/ui/UiInput.vue'
import { useAuthStore } from '../../stores/auth'
import type { TotpEnrolment } from '../../types/auth'

const { t } = useI18n()
const authStore = useAuthStore()

/** The enrolment in progress: a secret the user has been given but not yet confirmed. */
const enrolment: Ref<TotpEnrolment | null> = ref(null)

/** The code being typed, to confirm an enrolment or to turn the factor off. */
const code: Ref<string> = ref('')

/**
 * The recovery codes, held only for as long as this page is open.
 *
 * The backend stores hashes, so these cannot be shown again — not by us and not
 * by anyone reading the database. The screen says so, and this ref is why it is
 * true: nothing writes them anywhere.
 */
const recoveryCodes: Ref<string[] | null> = ref(null)

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
  code.value = ''
}

/**
 * Confirms the enrolment and shows the recovery codes, once.
 * @returns Resolves once the request has settled.
 */
const confirmEnrolment = async (): Promise<void> => {
  if (enrolment.value === null) {
    return
  }

  const codes = await authStore.confirmTwoFactorEnrolment(enrolment.value.secret, code.value)
  if (codes !== null) {
    recoveryCodes.value = codes.codes
    enrolment.value = null
    code.value = ''
  }
}

/**
 * Turns the second factor off, for a caller who can still satisfy it.
 * @returns Resolves once the request has settled.
 */
const disable = async (): Promise<void> => {
  if (await authStore.disableTwoFactor(code.value)) {
    code.value = ''
    recoveryCodes.value = null
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
  <section class="w-full max-w-2xl">
    <h1 class="text-3xl font-semibold tracking-title text-text-primary">
      {{ t('app.twoFactor.heading') }}
    </h1>
    <p class="mt-1 mb-4 text-base text-text-secondary">{{ t('app.twoFactor.subtitle') }}</p>

    <UiAlert v-if="authStore.errorMessage !== null" variant="error" class="mb-4">
      {{ authStore.errorMessage }}
    </UiAlert>

    <UiCard v-if="recoveryCodes !== null" class="mb-4">
      <h2 class="mb-1 text-xl font-semibold">{{ t('app.twoFactor.recoveryTitle') }}</h2>
      <p class="mb-3 text-base text-text-secondary">{{ t('app.twoFactor.recoveryWarning') }}</p>
      <ul class="mb-3 grid grid-cols-2 gap-1 font-mono text-base">
        <li v-for="recoveryCode in recoveryCodes" :key="recoveryCode">{{ recoveryCode }}</li>
      </ul>
      <UiButton variant="secondary" @click="copyRecoveryCodes">
        {{ t('app.twoFactor.copyCodes') }}
      </UiButton>
    </UiCard>

    <UiCard v-if="isEnrolling && enrolment !== null">
      <h2 class="mb-1 text-xl font-semibold">{{ t('app.twoFactor.enrolTitle') }}</h2>
      <p class="mb-3 text-base text-text-secondary">{{ t('app.twoFactor.enrolInstructions') }}</p>

      <!-- The secret is shown as text as well as being scannable, because the
           person setting this up is usually sitting at the machine showing the
           code and photographing their own screen is not always possible. -->
      <p class="mb-1 text-base text-text-secondary">{{ t('app.twoFactor.secretLabel') }}</p>
      <p class="mb-3 font-mono text-base break-all">{{ enrolment.secret }}</p>

      <UiForm @submit="confirmEnrolment">
        <div class="flex flex-col gap-3">
          <UiInput
            v-model="code"
            :label="t('app.twoFactor.confirmCodeLabel')"
            :placeholder="t('app.auth.codePlaceholder')"
            required
            autocomplete="one-time-code"
          />
          <div class="flex gap-2">
            <UiButton type="submit">{{ t('app.twoFactor.confirmEnrolment') }}</UiButton>
            <UiButton variant="secondary" @click="cancelEnrolment">
              {{ t('app.twoFactor.cancelEnrolment') }}
            </UiButton>
          </div>
        </div>
      </UiForm>
    </UiCard>

    <UiCard v-else>
      <h2 class="mb-1 text-xl font-semibold">{{ t('app.twoFactor.manageTitle') }}</h2>
      <p class="mb-3 text-base text-text-secondary">{{ t('app.twoFactor.manageDescription') }}</p>

      <UiButton class="mb-4" @click="beginEnrolment">{{ t('app.twoFactor.enable') }}</UiButton>

      <UiForm @submit="disable">
        <div class="flex flex-col gap-3">
          <p class="text-base text-text-secondary">{{ t('app.twoFactor.disableDescription') }}</p>
          <UiInput
            v-model="code"
            :label="t('app.twoFactor.disableCodeLabel')"
            :placeholder="t('app.auth.codePlaceholder')"
            required
            autocomplete="one-time-code"
          />
          <UiButton type="submit" variant="destructive">{{ t('app.twoFactor.disable') }}</UiButton>
        </div>
      </UiForm>
    </UiCard>
  </section>
</template>
