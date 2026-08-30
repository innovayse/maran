<script setup lang="ts">
/**
 * The sign-in screen. Two fields and one button: the panel has exactly one way in,
 * and anything else on this page would be a way in that is not this one.
 *
 * On a password that is right but incomplete — the user has a second factor — the
 * store keeps the username and this page routes to the code screen rather than
 * reporting a failure, because nothing has failed.
 */
import { ref, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiInput from '../../components/ui/UiInput.vue'
import UiPasswordInput from '../../components/ui/UiPasswordInput.vue'
import { useAuthStore } from '../../stores/auth'

const { t } = useI18n()
const router = useRouter()
const route = useRoute()
const authStore = useAuthStore()

/** The login name being typed. */
const username: Ref<string> = ref('')

/** The password being typed. Held only until the request is sent. */
const password: Ref<string> = ref('')

/**
 * Signs in, then goes wherever the visitor was originally headed.
 * @returns Resolves once the attempt has settled.
 */
const submit = async (): Promise<void> => {
  const signedIn = await authStore.login({ username: username.value, password: password.value })

  if (signedIn) {
    // `redirect` is whatever the guard captured when it turned the visitor away,
    // so a bookmark to a deep page survives the detour through this screen.
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
    await router.replace(redirect)
    return
  }

  if (authStore.twoFactorUsername !== null) {
    // The password travels in history state rather than being typed again: the
    // backend re-checks both factors together, and asking for it twice would cost
    // the user something and buy no security.
    await router.push({ name: 'login-two-factor', state: { password: password.value } })
  }
}
</script>

<template>
  <UiForm @submit="submit">
    <h2 class="text-xl font-semibold">{{ t('app.auth.signInTitle') }}</h2>
    <p class="mt-1 mb-4 text-base text-text-secondary">{{ t('app.auth.signInSubtitle') }}</p>

    <UiAlert v-if="authStore.errorMessage !== null" variant="error" class="mb-4">
      {{ authStore.errorMessage }}
    </UiAlert>

    <div class="flex flex-col gap-3">
      <UiInput
        v-model="username"
        :label="t('app.auth.usernameLabel')"
        :placeholder="t('app.auth.usernamePlaceholder')"
        required
        autocomplete="username"
      />

      <UiPasswordInput
        v-model="password"
        :label="t('app.auth.passwordLabel')"
        :placeholder="t('app.auth.passwordPlaceholder')"
        required
        autocomplete="current-password"
      />

      <UiButton class="mt-1 w-full justify-center" type="submit" :disabled="authStore.loading">
        {{ authStore.loading ? t('app.auth.signingIn') : t('app.auth.signIn') }}
      </UiButton>
    </div>
  </UiForm>
</template>
