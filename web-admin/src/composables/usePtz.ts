import { onBeforeUnmount, ref } from 'vue'
import { PtzLease } from '../lib/ptzLease'
import { errorMessage } from '../lib/format'
export function usePtz() {
  const active = ref(false), error = ref('')
  const lease = new PtzLease((moving, failure) => { active.value = moving; if (failure) error.value = errorMessage(failure); else if (moving) error.value = '' })
  const release = () => void lease.stop(), hidden = () => { if (document.hidden) release() }, unload = () => void lease.stop(true)
  const stopEvents = ['pointerup', 'pointercancel', 'blur', 'platform-media-stop', 'platform-access-changed']
  stopEvents.forEach(event => window.addEventListener(event, release))
  window.addEventListener('pagehide', unload); document.addEventListener('visibilitychange', hidden)
  onBeforeUnmount(() => { void lease.stop(); stopEvents.forEach(event => window.removeEventListener(event, release)); window.removeEventListener('pagehide', unload); document.removeEventListener('visibilitychange', hidden) })
  return { active, error, start: lease.start.bind(lease), stop: lease.stop.bind(lease) }
}
