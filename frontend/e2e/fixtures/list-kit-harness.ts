import { createApp, defineComponent, h, ref, type VNode } from 'vue'
import { useI18n } from 'vue-i18n'
import UiActiveFilter from '@/components/ui/UiActiveFilter.vue'
import UiFilterSheet from '@/components/ui/UiFilterSheet.vue'
import UiFilterTrigger from '@/components/ui/UiFilterTrigger.vue'
import UiPagination from '@/components/ui/UiPagination.vue'
import UiSelect from '@/components/ui/UiSelect.vue'
import { createAppI18n } from '@/i18n'
import type { AppLocale } from '@/types/app'
import '@/assets/css/main.css'

const params = new URLSearchParams(window.location.search)
const LOCALES: readonly AppLocale[] = ['en', 'ru', 'hy']
const askedLocale = params.get('locale') as AppLocale | null
const locale: AppLocale = askedLocale !== null && LOCALES.includes(askedLocale) ? askedLocale : 'en'
const startTotal = Number(params.get('total') ?? '312')
const startPage = Number(params.get('page') ?? '1')

// A stand-in field: the sheet's contract does not depend on what the fields are, only that they
// edit the draft. The option labels are fixture text, not product copy.
const STATUS_OPTIONS = [
  { value: '', label: '—' },
  { value: 'active', label: 'active' },
  { value: 'suspended', label: 'suspended' },
]

const Harness = defineComponent({
  setup: (): (() => VNode) => {
    const { t } = useI18n()
    const open = ref(false)
    const filters = ref<Record<string, string>>({ status: '' })
    const page = ref(startPage)
    const pageSize = ref(25)

    return (): VNode =>
      {return h('div', { style: 'display: flex; flex-direction: column; gap: 16px' }, [
        h(UiFilterTrigger, {
          label: t('common.list.filters'),
          activeCount: Object.values(filters.value).filter((value) => {return value !== ''}).length,
          activeLabel: t('common.list.filtersApplied', { n: 1 }, 1),
          onClick: () => {
            open.value = true
          },
        }),
        h(UiActiveFilter, {
          chips: Object.entries(filters.value)
            .filter(([, value]) => {return value !== ''})
            .map(([key, value]) => {return {
              key,
              text: value,
              removeLabel: t('common.list.removeFilter', { filter: value }),
            }}),
          clearLabel: t('common.list.clearAll'),
          onRemove: (key: string) => {
            filters.value = { ...filters.value, [key]: '' }
          },
          onClear: () => {
            filters.value = { status: '' }
          },
        }),
        h('p', { 'data-testid': 'applied' }, JSON.stringify(filters.value)),
        h(
          UiFilterSheet,
          {
            open: open.value,
            modelValue: filters.value,
            title: t('common.list.filters'),
            closeLabel: t('common.close'),
            applyLabel: t('common.list.apply'),
            resetLabel: t('common.list.reset'),
            'onUpdate:modelValue': (value: Record<string, string>) => {
              filters.value = value
            },
            onClose: () => {
              open.value = false
            },
          },
          {
            default: ({ draft }: { draft: Record<string, string> }) =>
              {return h(UiSelect, {
                label: 'Status',
                modelValue: draft.status ?? '',
                options: STATUS_OPTIONS,
                'onUpdate:modelValue': (value: string) => {
                  draft.status = value
                },
              })},
          },
        ),
        h(UiPagination, {
          page: page.value,
          pageSize: pageSize.value,
          total: startTotal,
          pageSizes: [10, 25, 50, 100],
          labels: {
            navigation: t('common.list.label'),
            first: t('common.list.first'),
            previous: t('common.list.previous'),
            next: t('common.list.next'),
            last: t('common.list.last'),
            pageSize: t('common.list.pageSize'),
            page: (n: number) => {return t('common.list.page', { page: n })},
            position: (n: number, pages: number) => {return t('common.list.position', { page: n, pages })},
            range: (from: number, to: number, total: number) => {return t('common.list.range', { from, to, total })},
          },
          'onUpdate:page': (value: number) => {
            page.value = value
          },
          'onUpdate:pageSize': (value: number) => {
            pageSize.value = value
            page.value = 1
          },
        }),
      ])}
  },
})

const i18n = createAppI18n()
i18n.global.locale.value = locale
document.documentElement.lang = locale
createApp(Harness).use(i18n).mount('#app')
