<script setup lang="ts">
/**
 * Create-or-change screen of a DNS zone template, for administrators: a name, a description and
 * the records a new zone starts with. The same page serves both: a route that carries a template
 * id loads it and saves over it, one without starts empty. `UiForm` renders `<form novalidate>`, so
 * the browser validates nothing; the name check is advice that saves a round trip, and whether the
 * records are acceptable, variables included, is the panel's decision, shown above the form in its
 * own already-localized words (rules/vue.md: forms, server errors).
 *
 * A record value may carry the variables the panel's catalogue lists. The list beside the form is
 * read from the panel, so the page names no variable and no rule of where one may be used.
 *
 * The records are edited as a draft and sent together with the name when the template is saved;
 * the panel replaces the stored set whole.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiInput from '../../components/ui/UiInput.vue'
import UiLink from '../../components/ui/UiLink.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiTextarea from '../../components/ui/UiTextarea.vue'
import DnsTemplateRecordsEditor from '../../components/dns/DnsTemplateRecordsEditor.vue'
import DnsTemplateVariables from '../../components/dns/DnsTemplateVariables.vue'
import { useDnsCatalogStore } from '../../stores/dnsCatalog'
import { useDnsTemplatesStore } from '../../stores/dnsTemplates'
import { flashNoticeState } from '../../utils/flashNotice'
import type { LoadingState } from '../../types/loading'
import type { DnsTemplateDetail, DnsTemplateRecord } from '../../types/dnsTemplate'

/** Props accepted by {@link DnsTemplateFormPage}. */
const props = defineProps<{
  /** The template to change, from the route; absent on the create route. */
  id?: string
}>()

const { t } = useI18n()
const router = useRouter()
const store = useDnsTemplatesStore()
const catalogStore = useDnsCatalogStore()

/** The template's name as typed. */
const name: Ref<string> = ref('')

/** The description as typed; may stay empty. */
const description: Ref<string> = ref('')

/** The draft's records. */
const records: Ref<DnsTemplateRecord[]> = ref([])

/** Whether a submit has been attempted, so nothing turns red before the person has tried. */
const submitted: Ref<boolean> = ref(false)

/** Whether the page changes an existing template rather than creating one. */
const isEdit: ComputedRef<boolean> = computed(() => {
  return props.id !== undefined
})

/** Validation message for the name, or `null`. */
const nameError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  return name.value.trim().length === 0 ? t('dns.templates.form.errors.nameRequired') : null
})

/** Where the template's read stands, for the loading region: a create has nothing to read. */
const regionState: ComputedRef<LoadingState> = computed(() => {
  if (!isEdit.value) {
    return 'loaded'
  }
  switch (store.detail.state) {
    case 'loading':
      return 'loading'
    case 'found':
      return 'loaded'
    default:
      return 'error'
  }
})

/** Whether the template is still on its way, so the form draws placeholders. */
const isLoading: ComputedRef<boolean> = computed(() => {
  return regionState.value === 'loading'
})

/** The page's title: a new template or the one being changed. */
const heading: ComputedRef<string> = computed(() => {
  return isEdit.value ? t('dns.templates.form.editHeading') : t('dns.templates.form.newHeading')
})

/**
 * Fills the draft from a template read from the panel.
 * @param template The template.
 * @returns Nothing.
 */
const fill = (template: DnsTemplateDetail): void => {
  name.value = template.name
  description.value = template.description
  records.value = template.records.map((record) => {
    return { ...record }
  })
}

/**
 * Validates, then submits. On success the list opens with a notice; on failure the store holds the
 * panel's own message and the template renders it verbatim.
 * @returns Resolves once the attempt has settled.
 */
const submit = async (): Promise<void> => {
  submitted.value = true
  if (nameError.value !== null) {
    return
  }
  const saved = await store.save(props.id ?? null, {
    name: name.value.trim(),
    description: description.value.trim(),
    records: records.value,
  })
  if (saved !== null) {
    const notice = isEdit.value
      ? t('dns.templates.form.saved', { name: saved.name })
      : t('dns.templates.form.created', { name: saved.name })
    await router.push({ name: 'dns-templates', state: flashNoticeState(notice) })
  }
}

/**
 * Leaves the form without saving.
 * @returns Resolves once navigation has been dispatched.
 */
const cancel = async (): Promise<void> => {
  await router.push({ name: 'dns-templates' })
}

onMounted(async () => {
  store.clearErrors()
  // The catalogue and the variables are help for the form: neither blocks it, and both are read
  // again on each visit because their descriptions are localized per request.
  const help = Promise.all([catalogStore.load(), store.loadVariables()])
  if (props.id !== undefined) {
    await store.loadOne(props.id)
    if (store.detail.state === 'found') {
      fill(store.detail.template)
    }
  }
  await help
})
</script>

<template>
  <section class="flex w-full max-w-4xl min-w-0 flex-col gap-4">
    <UiLoadingRegion :state="regionState">
      <!-- A 404 is the not-found state, never the alert: the panel answered, and what it said is
           that no such template exists. -->
      <template #error>
        <UiAlert v-if="store.detail.state === 'failed'" variant="error">
          {{ store.detail.message ?? t('common.list.unreachable') }}
        </UiAlert>
        <UiEmptyState
          v-else
          :title="t('dns.templates.form.notFoundTitle')"
          :description="t('dns.templates.form.notFoundDescription')"
        />
      </template>

      <div class="flex min-w-0 flex-col gap-4">
        <UiPageHeading :title="heading" :subtitle="t('dns.templates.form.subtitle')">
          <template #actions>
            <UiLink :to="{ name: 'dns-templates' }" icon="chevronLeft">{{ t('dns.templates.form.backToList') }}</UiLink>
          </template>
        </UiPageHeading>

        <!-- The one thing that needs attention goes first: the panel's own refusal, verbatim. -->
        <UiAlert v-if="store.saveErrorMessage !== null" variant="error">{{ store.saveErrorMessage }}</UiAlert>

        <UiCard>
          <UiForm :loading="isLoading" @submit="submit">
            <div class="flex flex-col gap-5">
              <UiInput
                v-model="name"
                :label="t('dns.templates.form.fields.name')"
                :placeholder="t('dns.templates.form.placeholders.name')"
                :error="nameError"
                required
              />
              <UiTextarea
                v-model="description"
                :label="t('dns.templates.form.fields.description')"
                :placeholder="t('dns.templates.form.placeholders.description')"
                :rows="2"
              />
              <DnsTemplateRecordsEditor v-if="!isLoading" v-model:records="records" :catalog="catalogStore.catalog" />
              <div class="flex flex-col-reverse gap-2 border-t border-border-subtle pt-5 sm:flex-row sm:justify-end">
                <UiButton variant="secondary" type="button" @click="cancel">{{ t('common.cancel') }}</UiButton>
                <UiButton type="submit" :disabled="store.saving">{{ t('dns.templates.form.submit') }}</UiButton>
              </div>
            </div>
          </UiForm>
        </UiCard>

        <DnsTemplateVariables :variables="store.variables" />
      </div>
    </UiLoadingRegion>
  </section>
</template>
