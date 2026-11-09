<script setup lang="ts">
/**
 * One site's options: which name is the main one, how large an upload may be, how long browsers may
 * keep static files, where old addresses are sent, who may open the site, which networks may reach it, and how often one visitor address may ask for pages.
 *
 * **The whole set is saved at once.** The panel replaces a site's options as a unit, so one Save sends
 * every section and what is not in the form is removed. The form is filled from what the panel holds
 * and again from what it answers with after a save, so a refusal keeps what was typed on screen.
 *
 * **Nothing here knows a limit or a choice.** The bounds and every select's list come with the
 * options (rules/architecture.md); the server's own refusal, in the caller's language, is shown under
 * the section it is about. The refusal's code picks the section and is never looked up for text.
 *
 * **Passwords never come back.** A user's password field is empty on load; the server keeps a hash
 * and returns user names only. Turning the first user on asks first, because from then on every
 * visitor needs a password.
 */
import { onMounted, ref, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiSkeleton from '../../components/ui/UiSkeleton.vue'
import SiteBasicAuthEditor from '../../components/sites/SiteBasicAuthEditor.vue'
import SiteCanonicalHostCard from '../../components/sites/SiteCanonicalHostCard.vue'
import SiteIpRulesEditor from '../../components/sites/SiteIpRulesEditor.vue'
import SiteLimitsCard from '../../components/sites/SiteLimitsCard.vue'
import SiteRedirectsEditor from '../../components/sites/SiteRedirectsEditor.vue'
import SiteRequestRateCard from '../../components/sites/SiteRequestRateCard.vue'
import { useSiteOptionsForm } from '../../composables/useSiteOptionsForm'
import { useSiteOptionsStore } from '../../stores/siteOptions'

/** Props accepted by {@link SiteOptionsTab}. */
const props = defineProps<{
  /** The site whose options are edited. */
  siteId: string
}>()

const { t } = useI18n()
const store = useSiteOptionsStore()
const {
  canonicalHost,
  bodySize,
  cacheDays,
  redirects,
  users,
  ipMode,
  ipText,
  rateText,
  burstText,
  options,
  generalError,
  turnsAuthOn,
  errorFor,
  numberErrorFor,
  buildRequest,
} = useSiteOptionsForm()

/** Whether the confirmation for turning basic authentication on is open. */
const confirmingAuth: Ref<boolean> = ref(false)

/**
 * Sends the form to the panel.
 * @returns Resolves once the request has settled.
 */
const send = async (): Promise<void> => {
  const request = buildRequest()
  if (request !== null) {
    await store.save(props.siteId, request)
  }
}

/**
 * Handles Save: asks first when this would be the first user the site gets, otherwise sends.
 * @returns Resolves once the request has settled or the question is open.
 */
const submit = async (): Promise<void> => {
  if (turnsAuthOn.value) {
    confirmingAuth.value = true
    return
  }
  await send()
}

/**
 * Sends after the operator confirmed turning protection on.
 * @returns Resolves once the request has settled.
 */
const confirmAuth = async (): Promise<void> => {
  await send()
  confirmingAuth.value = false
}

/**
 * Abandons the question without sending.
 * @returns Nothing.
 */
const cancelAuth = (): void => {
  confirmingAuth.value = false
}

/**
 * Loads the options for this site.
 * @returns Resolves once the request has settled.
 */
const load = async (): Promise<void> => {
  await store.load(props.siteId)
}

onMounted(load)
</script>

<template>
  <UiLoadingRegion :state="store.loadState">
    <template #error>
      <UiAlert variant="error">{{ store.errorMessage ?? t('app.status.unreachable') }}</UiAlert>
    </template>

    <UiForm :loading="store.loadState === 'loading'" class="flex flex-col gap-4" @submit="submit">
      <UiAlert v-if="generalError !== null" variant="error">{{ generalError }}</UiAlert>
      <UiAlert v-else-if="store.saved" variant="success">{{ t('siteOptions.saved') }}</UiAlert>

      <template v-if="options !== null">
        <SiteCanonicalHostCard
          v-model="canonicalHost"
          :choices="options.choices.canonicalHosts"
          :disabled="store.saving"
          :error="errorFor('canonicalHost')"
        />

        <SiteLimitsCard
          v-model:body-size="bodySize"
          v-model:cache-days="cacheDays"
          :limits="options.limits"
          :disabled="store.saving"
          :upload-error="numberErrorFor('uploadSize')"
          :cache-error="numberErrorFor('cache')"
        />

        <UiCard class="flex flex-col gap-4">
          <UiSectionHeading
            :title="t('siteOptions.redirects.heading')"
            :subtitle="t('siteOptions.redirects.subtitle')"
          />
          <SiteRedirectsEditor
            v-model="redirects"
            :matches="options.choices.redirectMatches"
            :statuses="options.choices.redirectStatuses"
            :maximum="options.limits.maximumRedirects"
            :disabled="store.saving"
            :error="errorFor('redirects')"
          />
        </UiCard>

        <UiCard class="flex flex-col gap-4">
          <UiSectionHeading
            :title="t('siteOptions.basicAuth.heading')"
            :subtitle="t('siteOptions.basicAuth.subtitle')"
          />
          <SiteBasicAuthEditor
            v-model="users"
            :maximum="options.limits.maximumBasicAuthUsers"
            :minimum-password-length="options.limits.minimumPasswordLength"
            :disabled="store.saving"
            :error="errorFor('basicAuth')"
          />
        </UiCard>

        <UiCard class="flex flex-col gap-4">
          <UiSectionHeading
            :title="t('siteOptions.ipRules.heading')"
            :subtitle="t('siteOptions.ipRules.subtitle')"
          />
          <SiteIpRulesEditor
            v-model:mode="ipMode"
            v-model:text="ipText"
            :modes="options.choices.ipAccessModes"
            :maximum="options.limits.maximumIpRules"
            :disabled="store.saving"
            :error="errorFor('ipRules')"
          />
        </UiCard>

        <SiteRequestRateCard
          v-model:per-second="rateText"
          v-model:burst="burstText"
          :limits="options.limits"
          :disabled="store.saving"
          :rate-error="numberErrorFor('requestRate')"
          :burst-error="numberErrorFor('requestBurst')"
        />

        <div>
          <UiButton type="submit" :disabled="store.saving">
            {{ store.saving ? t('siteOptions.saving') : t('siteOptions.save') }}
          </UiButton>
        </div>
      </template>

      <!-- The sections' real labels are not known until the panel answers (the choices come with the
           options), so one block holds the form's place. -->
      <UiCard v-else>
        <UiSkeleton shape="block" />
      </UiCard>
    </UiForm>

    <!-- Outside the form and bound to a value that really changes, like the page's own dialog: a
         dialog created already open never runs the modal's open-watcher, so focus would not enter it. -->
    <UiConfirm
      :open="confirmingAuth"
      :title="t('siteOptions.basicAuth.confirmTitle')"
      :question="t('siteOptions.basicAuth.confirmQuestion')"
      :confirm-label="t('siteOptions.basicAuth.confirmAction')"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting="store.saving"
      :acting-label="t('siteOptions.saving')"
      @close="cancelAuth"
      @confirm="confirmAuth"
    />
  </UiLoadingRegion>
</template>
