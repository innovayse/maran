<script setup lang="ts">
/**
 * Sets a new password from a reset link.
 *
 * The token comes from the query string, because that is where the mail put it, and
 * it is the only credential this screen has — nothing here names the account, and
 * naming one would let a caller aim a token at somebody else's password.
 *
 * The new-password field DOES offer to generate one: unlike the mail-settings
 * screen, a secret is being minted here rather than an existing one entered, and
 * generating also reveals the value so it can be recorded.
 *
 * A refusal is rendered exactly as the panel sent it, beside a way back to the
 * sign-in screen. The backend answers a token that never existed, one that has
 * expired and one already spent with the same message, and nothing on this page
 * inspects it: a screen that told them apart would be the disclosure the single
 * message exists to prevent.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiPasswordInput from '../../components/ui/UiPasswordInput.vue'
import { useAuthStore } from '../../stores/auth'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()

/** The password being set. */
const password: Ref<string> = ref('')

/** The same password typed again, so a typo does not become the new password. */
const confirmation: Ref<string> = ref('')

/** The panel's own message when the two entries differ, or `null` while they match. */
const mismatch: Ref<string | null> = ref(null)

/** True once the password has been changed. */
const done: Ref<boolean> = ref(false)

/** The token from the reset link, or the empty string when the link carried none. */
const token: ComputedRef<string> = computed(() => {
  return typeof route.query.token === 'string' ? route.query.token : ''
})

/**
 * Sets the new password.
 *
 * The two entries are compared here because that is a fact about this form rather
 * than about the panel; everything else — the token, the length — is the server's
 * to judge, and its already-localized refusal is what the screen shows.
 * @returns Resolves once the request has settled.
 */
const submit = async (): Promise<void> => {
  if (password.value !== confirmation.value) {
    mismatch.value = t('app.auth.passwordsDoNotMatch')
    return
  }

  mismatch.value = null
  done.value = await authStore.resetPassword({ token: token.value, newPassword: password.value })
}

/**
 * Returns to the sign-in screen — the way back a refused token leaves open.
 * @returns Resolves once the navigation has settled.
 */
const backToSignIn = async (): Promise<void> => {
  await router.push({ name: 'login' })
}
</script>

<template>
  <div>
    <h2 class="text-xl font-semibold">{{ t('app.passwordReset.resetTitle') }}</h2>
    <p class="mt-1 mb-4 text-base text-text-secondary">{{ t('app.passwordReset.resetSubtitle') }}</p>

    <template v-if="done">
      <UiAlert variant="info" class="mb-4">{{ t('app.passwordReset.resetDone') }}</UiAlert>
      <UiButton class="w-full justify-center" @click="backToSignIn">
        {{ t('app.passwordReset.backToSignIn') }}
      </UiButton>
    </template>

    <template v-else>
      <!-- The refusal as the backend worded it, and never a word about which kind of
           refusal it was. The button beneath is the way back: a dead end here would
           leave somebody with a stale link and no route to the sign-in screen. -->
      <UiAlert v-if="authStore.errorMessage !== null" variant="error" class="mb-4">
        {{ authStore.errorMessage }}
      </UiAlert>

      <UiForm @submit="submit">
        <div class="flex flex-col gap-3">
          <UiPasswordInput
            v-model="password"
            :label="t('app.auth.passwordLabel')"
            required
            autocomplete="new-password"
            generate
          />
          <UiPasswordInput
            v-model="confirmation"
            :label="t('app.auth.confirmPasswordLabel')"
            :error="mismatch"
            required
            autocomplete="new-password"
          />
          <UiButton class="mt-1 w-full justify-center" type="submit" :disabled="authStore.loading">
            {{ t('app.passwordReset.setPassword') }}
          </UiButton>
          <UiButton variant="ghost" class="justify-center" @click="backToSignIn">
            {{ t('app.passwordReset.backToSignIn') }}
          </UiButton>
        </div>
      </UiForm>
    </template>
  </div>
</template>
