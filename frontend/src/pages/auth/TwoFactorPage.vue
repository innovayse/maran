<script setup lang="ts">
/**
 * The second step of signing in: the code from the authenticator app, or one of
 * the recovery codes kept for the day the phone is gone.
 *
 * Reaching this page directly, without having passed the password step, sends the
 * visitor back to the sign-in screen — the store holds no username, so there is
 * nothing here to complete.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiForm from '../../components/ui/UiForm.vue'
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
  <UiForm @submit="submit">
    <h2 class="text-xl font-semibold">{{ t('app.auth.twoFactorTitle') }}</h2>
    <p class="mt-1 mb-4 text-base text-text-secondary">{{ t('app.auth.twoFactorSubtitle') }}</p>

    <UiAlert v-if="authStore.errorMessage !== null" variant="error" class="mb-4">
      {{ authStore.errorMessage }}
    </UiAlert>

    <div class="flex flex-col gap-3">
      <UiInput
        v-model="code"
        :label="codeLabel"
        :placeholder="codePlaceholder"
        required
        autocomplete="one-time-code"
      />

      <UiButton class="mt-1 w-full justify-center" type="submit" :disabled="authStore.loading">
        {{ authStore.loading ? t('app.auth.signingIn') : t('app.auth.verify') }}
      </UiButton>

      <UiButton class="w-full justify-center" variant="ghost" @click="toggleRecoveryCode">
        {{ usingRecoveryCode ? t('app.auth.useAuthenticatorCode') : t('app.auth.useRecoveryCode') }}
      </UiButton>
    </div>
  </UiForm>
</template>
