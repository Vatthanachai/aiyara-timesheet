/**
 * What the Timesheet remote keeps on this device (see the Mobile And Offline Plan): how many changes it holds that
 * the server has not accepted, as the remote last reported, and the deletion of all of it at sign-out. The data
 * belongs to the remote's origin, so it is deleted by opening the remote in a hidden frame and waiting for its reply.
 */
const usedKey = 'aiyara.offline-used'

export function useOnline() {
  return useState<boolean>('online', () => true)
}

export function useOfflineData() {
  const pending = useState<number>('offline-pending', () => 0)
  const config = useRuntimeConfig()

  /** The remote reported its count: remember that this browser holds offline data, so sign-out knows to clear it. */
  function report(count: number) {
    pending.value = count
    try { localStorage.setItem(usedKey, '1') } catch { /* the flag is only an optimization */ }
  }

  /** Deletes the remote's offline data (every account's). Never throws, and gives up after three seconds. */
  async function clear(): Promise<void> {
    pending.value = 0
    if (!import.meta.client) return
    try {
      if (localStorage.getItem(usedKey) !== '1') return
      localStorage.removeItem(usedKey)
    } catch { /* clear anyway */ }
    const url = new URL(config.public.remotes.timesheet)
    url.searchParams.set('aiyara-purge', '1')
    await new Promise<void>(resolve => {
      const frame = document.createElement('iframe')
      frame.hidden = true
      frame.setAttribute('aria-hidden', 'true')
      const finish = () => { window.clearTimeout(timeout); window.removeEventListener('message', receive); frame.remove(); resolve() }
      const timeout = window.setTimeout(finish, 3000)
      function receive(event: MessageEvent) {
        if (event.origin !== url.origin || event.source !== frame.contentWindow) return
        // The frame asks first; only this answer makes it delete anything.
        if (event.data?.type === 'aiyara.purge-ready') frame.contentWindow?.postMessage({ type: 'aiyara.purge' }, url.origin)
        else if (event.data?.type === 'aiyara.offline-cleared') finish()
      }
      window.addEventListener('message', receive)
      frame.src = url.toString()
      document.body.appendChild(frame)
    })
  }

  return { pending, report, clear }
}
