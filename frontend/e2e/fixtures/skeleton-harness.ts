import { computed, createApp, defineComponent, h, ref, type VNode } from 'vue'
import UiAlert from '@/components/ui/UiAlert.vue'
import UiCheckbox from '@/components/ui/UiCheckbox.vue'
import UiCombobox from '@/components/ui/UiCombobox.vue'
import UiDescriptionItem from '@/components/ui/UiDescriptionItem.vue'
import UiDescriptionList from '@/components/ui/UiDescriptionList.vue'
import UiForm from '@/components/ui/UiForm.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiLoadingRegion from '@/components/ui/UiLoadingRegion.vue'
import type { LoadingState } from '@/types/loading'
import UiSelect from '@/components/ui/UiSelect.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'
import UiSwitch from '@/components/ui/UiSwitch.vue'
import UiTable from '@/components/ui/UiTable.vue'
import type { UiTableColumn } from '@/types/tableColumn'
import UiTextarea from '@/components/ui/UiTextarea.vue'
import { createAppI18n } from '@/i18n'
import type { AppLocale } from '@/types/app'
import '@/assets/css/main.css'

const params = new URLSearchParams(window.location.search)
const LOCALES: readonly AppLocale[] = ['en', 'ru', 'hy']
const askedLocale = params.get('locale') as AppLocale | null
const locale: AppLocale = askedLocale !== null && LOCALES.includes(askedLocale) ? askedLocale : 'en'
const delay = params.get('delay') ?? '600'
const outcome: LoadingState = params.get('outcome') === 'error' ? 'error' : 'loaded'

/** One fixture row; the texts are fixture data, not product copy. */
interface HarnessRow {
  id: string
  name: string
}

// One column of every kind, so the spec can find each kind's placeholder.
const COLUMNS: readonly UiTableColumn[] = [
  { key: 'identity', label: 'Identity', kind: 'identity', sortField: 'name' },
  { key: 'identifier', label: 'Identifier', kind: 'identifier' },
  { key: 'text', label: 'Text', kind: 'text' },
  { key: 'mono', label: 'Mono', kind: 'mono' },
  { key: 'code', label: 'Code', kind: 'code' },
  { key: 'badge', label: 'Badge', kind: 'badge' },
  { key: 'number', label: 'Number', kind: 'number' },
  { key: 'date', label: 'Date', kind: 'date' },
  { key: 'actions', label: 'Actions', kind: 'actions' },
]

const ROWS: readonly HarnessRow[] = [
  { id: 'a', name: 'alpha' },
  { id: 'b', name: 'beta' },
]

const Harness = defineComponent({
  setup: (): (() => VNode) => {
    const state = ref<LoadingState>('loading')
    const loading = computed((): boolean => {
      return state.value === 'loading'
    })
    if (delay !== 'never') {
      setTimeout((): void => {
        state.value = outcome
      }, Number(delay))
    }

    /**
     * Renders every schema cell as the row's name, tagged with its column.
     * @param key The column key.
     * @returns The slot function for that column.
     */
    const cell = (key: string): ((scope: { row: HarnessRow }) => VNode) => {
      return ({ row }: { row: HarnessRow }): VNode => {
        return h('span', { 'data-testid': `cell-${key}` }, row.name)
      }
    }

    return (): VNode => {
      return h(
        UiLoadingRegion,
        { state: state.value },
        {
          error: (): VNode => {
            return h(UiAlert, { variant: 'danger' }, { default: (): string => {return 'The fixture failed to load.'} })
          },
          default: (): VNode[] => {
            return [
              h(
                UiTable,
                {
                  caption: 'Fixture table',
                  columns: COLUMNS,
                  rows: ROWS,
                  loading: loading.value,
                  skeletonRows: 3,
                },
                Object.fromEntries(
                  COLUMNS.map((column) => {
                    return [`cell-${column.key}`, cell(column.key)]
                  }),
                ),
              ),
              h(
                UiForm,
                { loading: loading.value, 'data-testid': 'harness-form' },
                {
                  default: (): VNode[] => {
                    return [
                      h(UiInput, { label: 'Input label', modelValue: 'value' }),
                      h(UiSelect, { label: 'Select label', modelValue: '', options: [] }),
                      h(UiTextarea, { label: 'Textarea label', modelValue: '' }),
                      h(UiCombobox, {
                        label: 'Combobox label',
                        searchLabel: 'Search',
                        modelValue: '',
                        options: [],
                        total: 0,
                      }),
                      h(UiSwitch, { label: 'Switch label', modelValue: true }),
                      h(UiCheckbox, { label: 'Checkbox label', modelValue: false }),
                    ]
                  },
                },
              ),
              h(
                UiDescriptionList,
                { loading: loading.value },
                {
                  default: (): VNode => {
                    return h(UiDescriptionItem, { term: 'Term label' }, { default: (): string => {return 'Term value'} })
                  },
                },
              ),
              loading.value ? h(UiSkeleton, { shape: 'kpi' }) : h('p', 'Figure loaded'),
              loading.value ? h(UiSkeleton, { shape: 'card' }) : h('p', 'Card loaded'),
            ]
          },
        },
      )
    }
  },
})

const i18n = createAppI18n()
i18n.global.locale.value = locale
document.documentElement.lang = locale
createApp(Harness).use(i18n).mount('#app')
