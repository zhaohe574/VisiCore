import { onBeforeUnmount, onMounted, ref, shallowRef } from 'vue'
import type { Page, Query, EventKind } from '../api'
import { errorMessage } from '../lib/format'
import { useEvents } from '../stores/events'

export function usePaged<T>(loader: (query: Query, signal?: AbortSignal) => Promise<Page<T>>, filters: () => Query = () => ({}), kinds: EventKind[] = [], enabled: () => boolean = () => true) {
  const items = shallowRef<T[]>([]), total = ref(0), page = ref(1), pageSize = ref(20), search = ref(''), loading = ref(false), error = ref('')
  let controller: AbortController | undefined
  let generation = 0
  async function load(reset = false) {
    if (!enabled()) { items.value = []; total.value = 0; return }
    if (reset) page.value = 1
    controller?.abort(); controller = new AbortController()
    const current = ++generation
    loading.value = true; error.value = ''
    try {
      const data = await loader({ ...filters(), search: search.value, page: page.value, pageSize: pageSize.value }, controller.signal)
      if (current !== generation) return
      if (page.value > 1 && !data.items.length && data.total > 0) { page.value--; return load() }
      items.value = data.items; total.value = data.total
    } catch (e) { if (current === generation && !(e instanceof Error && e.name === 'AbortError')) error.value = errorMessage(e) }
    finally { if (current === generation) loading.value = false }
  }
  const unsubscribe = useEvents().subscribe([...kinds, 'reconnected'], () => load())
  onMounted(() => void load())
  onBeforeUnmount(() => { generation++; controller?.abort(); unsubscribe() })
  return { items, total, page, pageSize, search, loading, error, load }
}
