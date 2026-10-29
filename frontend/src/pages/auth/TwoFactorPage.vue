<script setup lang="ts">
/**
 * The second step of signing in: the code from the authenticator app, or one of
 * the recovery codes kept for the day the phone is gone.
 *
 * Rendered inside the auth layout's centred panel: an accent icon square, the display-type
 * title, the server's error (verbatim) above the single field, then the pill action and the
 * switch between the two kinds of code. The field keeps `autocomplete="one-time-code"`, and
 * its label names which kind of code is expected so assistive technology hears the change.
 *
 * Reaching this page directly, without having passed the password step, sends the
 * visitor back to the sign-in screen — the store holds no username, so there is
 * nothing here to complete.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiAuthHeading from '../../components/ui/UiAuthHeading.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiIconTile from '../../components/ui/UiIconTile.vue'
import UiInput from '../../components/ui/UiInput.vue'
import { useAuthStore } from '../../stores/auth'

const { t } = useI18n()
const router = useRouter()
const authStore = useAuthStore()

/** The code being typed. */
const code: Ref<string> = ref('')

/** True while the user is entering a recovery code instead of a generated one. */
const usingRecoveryCode: Ref<boolean> = ref(false)

/** The password carried from the first step, in history state. */
const password: Ref<string> = ref('')

/** The field's label, which names which kind of code is expected. */
const codeLabel: ComputedRef<string> = computed(() => {
  return usingRecoveryCode.value ? t('app.auth.recoveryCodeLabel') : t('app.auth.codeLabel')
})

/** The field's placeholder, matching the label. */
const codePlaceholder: ComputedRef<string> = computed(() => {
  return usingRecoveryCode.value ? t('app.auth.recoveryCodePlaceholder') : t('app.auth.codePlaceholder')
})

/**
 * Swaps between the authenticator code and a recovery code.
 * @returns Nothing; the field's label, placeholder and hint change synchronously.
 */
const toggleRecoveryCode = (): void => {
  usingRecoveryCode.value = !usingRecoveryCode.value
  code.value = ''
}

/**
 * Completes the sign-in.
 * @returns Resolves once the attempt has settled.
 */
const submit = async (): Promise<void> => {
  if (await authStore.verifyTwoFactor(password.value, code.value)) {
    await router.replace('/')
  }
}

onMounted(async () => {
  // History state is `any` by contract; narrowing it here keeps the rest of the
  // page honest about what it actually received.
  const state = window.history.state as { password?: unknown } | null
  password.value = typeof state?.password === 'string' ? state.password : ''

  if (authStore.twoFactorUsername === null || password.value === '') {
    await router.replace({ name: 'login' })
  }
})
</script>

<template>
  <UiForm class="flex flex-col gap-5" @submit="submit">
    <!-- The heading stays an h2: the auth layout owns the screen's chrome and the sign-in
         screen's own title is asserted at that level, so the two steps read as one flow. -->
    <header class="flex flex-col items-start gap-4">
      <UiIconTile name="keyRound" size="lg" />
      <UiAuthHeading :title="t('app.auth.twoFactorTitle')" :subtitle="t('app.auth.twoFactorSubtitle')" />
    </header>

    <UiAlert v-if="authStore.errorMessage !== null" variant="error">
      {{ authStore.errorMessage }}
    </UiAlert>

    <!-- `one-time-code` lets a phone offer the code from the authenticator or a text message.
         `inputmode` follows the label: the 6-digit code gets the numeric keypad, but a recovery
         code carries letters, so it must keep the full keyboard. -->
    <UiInput
      v-model="code"
      :label="codeLabel"
      icon="keyRound"
      :placeholder="codePlaceholder"
      required
      autocomplete="one-time-code"
      :inputmode="usingRecoveryCode ? 'text' : 'numeric'"
    />

    <div class="flex flex-col gap-2">
      <UiButton class="w-full justify-center gap-2" type="submit" :disabled="authStore.loading">
        {{ authStore.loading ? t('app.auth.signingIn') : t('app.auth.verify') }}
        <UiIcon v-if="!authStore.loading" name="arrowRight" size="sm" />
      </UiButton>

      <UiButton class="w-full justify-center" variant="ghost" @click="toggleRecoveryCode">
        {{ usingRecoveryCode ? t('app.auth.useAuthenticatorCode') : t('app.auth.useRecoveryCode') }}
      </UiButton>
    </div>
  </UiForm>
</template>
