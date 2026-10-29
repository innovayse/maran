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
import UiAuthHeading from '../../components/ui/UiAuthHeading.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
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
    <UiAuthHeading class="mb-6" :title="t('app.auth.signInTitle')" :subtitle="t('app.auth.signInSubtitle')" />

    <!-- The server's own wording, verbatim and never rewritten here: the page adds no hint about
         whether the name or the password was the wrong half. -->
    <UiAlert v-if="authStore.errorMessage !== null" variant="error" class="mb-5">
      {{ authStore.errorMessage }}
    </UiAlert>

    <div class="flex flex-col gap-5">
      <UiInput
        v-model="username"
        :label="t('app.auth.usernameLabel')"
        icon="user"
        :placeholder="t('app.auth.usernamePlaceholder')"
        required
        autocomplete="username"
      />

      <UiPasswordInput
        v-model="password"
        :label="t('app.auth.passwordLabel')"
        icon="lock"
        :placeholder="t('app.auth.passwordPlaceholder')"
        required
        autocomplete="current-password"
      />

      <!-- The one primary pill on the screen; everything else here is quieter than it. -->
      <UiButton class="w-full justify-center" type="submit" :disabled="authStore.loading">
        {{ authStore.loading ? t('app.auth.signingIn') : t('app.auth.signIn') }}
        <UiIcon name="arrowRight" size="sm" />
      </UiButton>

      <!-- The panel mints reset tokens, and until this link existed there was no
           screen from which to ask for one. It only navigates, so it is a link. -->
      <UiButton variant="ghost" class="justify-center" :to="{ name: 'forgot-password' }">
        {{ t('app.passwordReset.forgotPassword') }}
      </UiButton>
    </div>
  </UiForm>
</template>
