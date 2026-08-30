<script setup lang="ts">
/**
 * First-run setup: the one screen a panel with no administrator will show, for
 * whatever route is asked for. The token is prefilled from the installer's
 * one-time link so an operator who followed it types only their own details.
 *
 * The strength meter is advice. The server's validator is the authority, and a
 * disagreement between them is a bug in this page, never a reason to submit.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiInput from '../../components/ui/UiInput.vue'
import UiPasswordInput from '../../components/ui/UiPasswordInput.vue'
import { useAuthStore } from '../../stores/auth'

/** The shortest password the backend accepts. Mirrors its validator, and is only a hint. */
const MIN_PASSWORD_LENGTH = 12

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()

/** The one-time token, prefilled from the installer's link. */
const token: Ref<string> = ref('')

/** The administrator's login name. */
const username: Ref<string> = ref('')

/** The administrator's contact address. */
const email: Ref<string> = ref('')

/** The chosen password. */
const password: Ref<string> = ref('')

/** The password typed a second time, so a typo does not become the only way in. */
const confirmation: Ref<string> = ref('')

/** Whether the password is long enough for the backend to accept it. */
const isLongEnough: ComputedRef<boolean> = computed(() => {
  return password.value.length >= MIN_PASSWORD_LENGTH
})

/** The advisory strength label shown under the password field. */
const strengthLabel: ComputedRef<string> = computed(() => {
  if (password.value.length === 0) {
    return ''
  }

  if (!isLongEnough.value) {
    return t('app.auth.passwordStrengthWeak')
  }

  return password.value.length >= 20
    ? t('app.auth.passwordStrengthStrong')
    : t('app.auth.passwordStrengthFair')
})

/** The mismatch message shown under the confirmation field, or null while it matches. */
const confirmationError: ComputedRef<string | null> = computed(() => {
  return confirmation.value.length > 0 && confirmation.value !== password.value
    ? t('app.auth.passwordsDoNotMatch')
    : null
})

/**
 * Creates the administrator and sends the operator to the sign-in screen.
 * @returns Resolves once the attempt has settled.
 */
const submit = async (): Promise<void> => {
  if (confirmationError.value !== null) {
    return
  }

  const created = await authStore.completeSetup({
    token: token.value,
    username: username.value,
    email: email.value,
    password: password.value,
  })

  if (created) {
    // Deliberately not signed in automatically: the operator has just chosen a
    // password, and typing it once proves it is the one they meant to set.
    await router.replace({ name: 'login' })
  }
}

onMounted(() => {
  token.value = typeof route.query.token === 'string' ? route.query.token : ''
})
</script>

<template>
  <UiForm @submit="submit">
    <h2 class="text-xl font-semibold">{{ t('app.auth.setupTitle') }}</h2>
    <p class="mb-4 text-base text-text-secondary">{{ t('app.auth.setupSubtitle') }}</p>

    <UiAlert v-if="authStore.errorMessage !== null" variant="error" class="mb-4">
      {{ authStore.errorMessage }}
    </UiAlert>

    <div class="flex flex-col gap-3">
      <UiInput
        v-model="token"
        :label="t('app.auth.setupTokenLabel')"
        :placeholder="t('app.auth.setupTokenPlaceholder')"
        required
      />

      <UiInput
        v-model="username"
        :label="t('app.auth.usernameLabel')"
        :placeholder="t('app.auth.usernamePlaceholder')"
        required
        autocomplete="username"
      />

      <UiInput
        v-model="email"
        type="email"
        :label="t('app.auth.emailLabel')"
        :placeholder="t('app.auth.emailPlaceholder')"
        required
        autocomplete="email"
      />

      <div class="flex flex-col gap-1">
        <UiPasswordInput
          v-model="password"
            :label="t('app.auth.passwordLabel')"
          :placeholder="t('app.auth.passwordPlaceholder')"
          required
          autocomplete="new-password"
        />
        <p v-if="strengthLabel !== ''" class="text-sm text-text-muted">{{ strengthLabel }}</p>
      </div>

      <UiPasswordInput
        v-model="confirmation"
        :label="t('app.auth.confirmPasswordLabel')"
        :placeholder="t('app.auth.passwordPlaceholder')"
        :error="confirmationError"
        required
        autocomplete="new-password"
      />

      <UiButton class="mt-1 w-full justify-center" type="submit" :disabled="authStore.loading">
        {{ authStore.loading ? t('app.auth.creating') : t('app.auth.createAdministrator') }}
      </UiButton>
    </div>
  </UiForm>
</template>
