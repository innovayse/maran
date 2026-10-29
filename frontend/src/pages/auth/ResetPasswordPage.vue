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
 *
 * "The link no longer works" is still a STATED state, not a generic failure: the
 * refusal sits in its own alert with the way forward beside it — ask for a new
 * link — and the way back. The alert never says which kind of dead link it was.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiAuthHeading from '../../components/ui/UiAuthHeading.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiPasswordInput from '../../components/ui/UiPasswordInput.vue'
import { useAuthStore } from '../../stores/auth'

const { t } = useI18n()
const route = useRoute()
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
</script>

<template>
  <div>
    <UiAuthHeading
      class="mb-6"
      :title="t('app.passwordReset.resetTitle')"
      :subtitle="t('app.passwordReset.resetSubtitle')"
    />

    <div v-if="done" class="flex flex-col gap-5">
      <UiAlert variant="success">{{ t('app.passwordReset.resetDone') }}</UiAlert>
      <UiButton class="w-full justify-center" :to="{ name: 'login' }">
        {{ t('app.passwordReset.backToSignIn') }}
        <UiIcon name="arrowRight" size="sm" />
      </UiButton>
    </div>

    <template v-else>
      <!-- The refusal as the backend worded it, and never a word about which kind of
           refusal it was. Beside it, the way forward: a stale link is cured by asking for a
           new one, and a dead end here would leave somebody with no route to do that. -->
      <UiAlert v-if="authStore.errorMessage !== null" variant="error" class="mb-5">
        {{ authStore.errorMessage }}
        <template #actions>
          <UiButton variant="ghost" :to="{ name: 'forgot-password' }">
            {{ t('app.passwordReset.requestNewLink') }}
          </UiButton>
        </template>
      </UiAlert>

      <UiForm @submit="submit">
        <div class="flex flex-col gap-5">
          <UiPasswordInput
            v-model="password"
            :label="t('app.auth.passwordLabel')"
            icon="lock"
            required
            autocomplete="new-password"
            generate
          />
          <UiPasswordInput
            v-model="confirmation"
            :label="t('app.auth.confirmPasswordLabel')"
            icon="lock"
            :error="mismatch"
            required
            autocomplete="new-password"
          />

          <!-- The one primary pill on the screen; the way back is quieter than it. -->
          <UiButton class="w-full justify-center" type="submit" :disabled="authStore.loading">
            {{ t('app.passwordReset.setPassword') }}
            <UiIcon name="arrowRight" size="sm" />
          </UiButton>
          <UiButton variant="ghost" class="justify-center" :to="{ name: 'login' }">
            {{ t('app.passwordReset.backToSignIn') }}
          </UiButton>
        </div>
      </UiForm>
    </template>
  </div>
</template>
