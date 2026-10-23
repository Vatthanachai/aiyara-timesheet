import { closedDatesOf, errorCodeOf, type Entry, type WorkHours } from '../utils/timesheet'
import { cacheGet, cachePut, isOfflineError, kvDelete, kvGet, kvSet, localError, offlineError, offlineSupported, outboxList, outboxRemove, outboxStore,
  planChange, purge, withQueue, zonedParts, type Change, type EntryBody, type OutboxItem } from '../utils/offline'

type Api = <T>(path: string, options?: { method?: string; body?: object }) => Promise<T>
type RunningTimer = { id: string; startedAtUtc: string; taskName: string | null; detail: string | null; notes: string | null
  projectId: string | null; categoryId: string | null; personalTaskId: string | null; includesBreak: boolean }
type TimerState = { timer: RunningTimer | null; nowUtc: string }
type ContextAnswer = { userId?: string; timeZoneId?: string }

/** The tab remembers which account the page last confirmed, to open its saved data when a reload finds no network. */
const identityKey = 'aiyara.offline.identity'
const statusOf = (cause: unknown) => (cause as { status?: number; statusCode?: number; data?: { statusCode?: number }; response?: { status?: number } } | null)
  ?.status ?? (cause as { data?: { statusCode?: number } } | null)?.data?.statusCode ?? (cause as { response?: { status?: number } } | null)?.response?.status
/** Trouble that passes by itself: the server is busy or down, or the session needs a sign-in. The item stays queued. */
const transient = (cause: unknown) => { const status = statusOf(cause) ?? 0; return status >= 500 || status === 429 || status === 401 || errorCodeOf(cause) === 'UNAVAILABLE' }

/**
 * Offline support for the Timesheet page (decisions 6 to 13 of the Mobile And Offline Plan): the identity of the
 * stored data, reads that fall back to the last saved answer, the outbox of entry changes, the local timer,
 * and the sync. `api` is the page's own call, so a sync uses the live session and its token refresh; nothing
 * here ever holds a credential.
 */
export function useOfflineQueue(deps: { api: Api; tenantId: Ref<string>; shellOrigin: string; workHours: () => WorkHours
  /** Reload the page's data: called after changes synced and after the connection came back. */
  refresh: () => Promise<void> }) {
  const online = ref(true)
  const identity = ref('')
  /** True when the page is offline and has no confirmed account, so it can show or send nothing. */
  const identityUnknown = ref(false)
  const items = ref<OutboxItem[]>([])
  const syncing = ref(false)
  /** The last sync stopped on trouble that passes by itself, so items still wait. */
  const retrying = ref(false)
  /** The page is showing the last saved copy of something it could not fetch. */
  const stale = ref(false)
  const timeZoneId = ref('')
  const supported = offlineSupported()
  const canQueue = computed(() => supported && Boolean(identity.value))
  const pending = computed(() => items.value.filter(item => item.status === 'pending'))
  const failed = computed(() => items.value.filter(item => item.status === 'failed'))
  let sendingSeq: number | null = null
  let timer: ReturnType<typeof setInterval> | undefined

  /** A store failure (private mode, a full disk) never breaks the page: it works online as before. */
  async function safe<T>(work: () => Promise<T>, fallback: T): Promise<T> {
    try { return await work() } catch { return fallback }
  }

  async function useIdentity(userId: string) {
    const next = `${deps.tenantId.value}:${userId}`
    try { sessionStorage.setItem(identityKey, next) } catch { /* the tab keeps no memory of it */ }
    if (next === identity.value) return
    // Nothing of the previous account may be sent under the new session while its data is being deleted.
    items.value = []
    identity.value = next
    identityUnknown.value = false
    if (!supported) return
    // Another account's cache and queue are deleted before anything is read (decision 13).
    await safe(() => purge(next), undefined)
    items.value = await safe(() => outboxList(next), [])
  }

  /** The member's context. It names the account, so it is the first read; offline, the tab's remembered account stands in. */
  async function readContext<T extends ContextAnswer>(fetcher: () => Promise<T>): Promise<T> {
    try {
      const value = await fetcher()
      await useIdentity(value.userId ?? 'unknown')
      if (value.timeZoneId) timeZoneId.value = value.timeZoneId
      stale.value = false
      void safe(() => cachePut(identity.value, 'context', value), undefined)
      // A page opened with changes still waiting (a reload, a new session of the same account) sends them now.
      if (pending.value.length) void sync()
      return value
    } catch (cause) {
      if (!isOfflineError(cause) || !supported) throw cause
      let remembered = ''
      try { remembered = sessionStorage.getItem(identityKey) ?? '' } catch { /* none */ }
      if (!remembered.startsWith(`${deps.tenantId.value}:`)) { identityUnknown.value = true; throw cause }
      await useIdentity(remembered.slice(deps.tenantId.value.length + 1))
      const saved = await safe(() => cacheGet<T>(identity.value, 'context'), undefined)
      if (!saved) throw cause
      if (saved.timeZoneId) timeZoneId.value = saved.timeZoneId
      stale.value = true
      return saved
    }
  }

  /** A read that falls back to the last saved answer when the network gives none. */
  async function readThrough<T>(name: string, fetcher: () => Promise<T>): Promise<T> {
    try {
      const value = await fetcher()
      if (identity.value) void safe(() => cachePut(identity.value, name, value), undefined)
      return value
    } catch (cause) {
      if (!isOfflineError(cause) || !identity.value) throw cause
      const saved = await safe(() => cacheGet<T>(identity.value, name), undefined)
      if (saved === undefined) throw cause
      stale.value = true
      return saved
    }
  }

  /** The month's entries with the waiting changes laid over them. */
  const overlay = (entries: Entry[], month: string) => withQueue(entries, items.value, month, deps.workHours())

  // --- The outbox -------------------------------------------------------------------------------------

  async function enqueue(change: Change) {
    const plan = planChange(items.value, change, identity.value, sendingSeq)
    if (plan.remove !== undefined) {
      await outboxRemove(plan.remove)
      items.value = items.value.filter(item => item.seq !== plan.remove)
    }
    if (plan.put) {
      const stored = await outboxStore(plan.put)
      items.value = items.value.some(item => item.seq === stored.seq) ? items.value.map(item => item.seq === stored.seq ? stored : item) : [...items.value, stored]
    }
  }

  /**
   * Sends a change now when the device is online and nothing for that entry waits (so errors show as they always
   * have); otherwise queues it. A request that gets no answer is queued too: its id makes a replay harmless.
   */
  async function submit(change: Change, send: () => Promise<unknown>): Promise<'sent' | 'queued'> {
    const waiting = items.value.some(item => item.entryId === change.entryId)
    if (!canQueue.value || (!waiting && online.value)) {
      try { await send(); return 'sent' } catch (cause) { if (!isOfflineError(cause) || !canQueue.value) throw cause }
    }
    await enqueue(change)
    void sync()
    return 'queued'
  }

  /** A failed update whose entry is gone is fixed as a new entry: the old item goes, and the change becomes a create. */
  async function fixAsNew(entryId: string): Promise<string | null> {
    const gone = items.value.find(item => item.entryId === entryId && item.kind === 'update' && item.status === 'failed' && item.error?.status === 404)
    if (!gone?.seq) return null
    await outboxRemove(gone.seq)
    items.value = items.value.filter(item => item.seq !== gone.seq)
    return crypto.randomUUID()
  }

  async function send(item: OutboxItem) {
    const gone = (cause: unknown) => statusOf(cause) === 404
    if (item.kind === 'create') {
      await deps.api('/entries', { method: 'POST', body: { ...item.body, id: item.entryId } })
      if (item.discardTimer) await deps.api('/timer', { method: 'DELETE' }).catch(cause => { if (!gone(cause)) throw cause })
    } else if (item.kind === 'update') await deps.api(`/entries/${item.entryId}`, { method: 'PUT', body: item.body! })
    else if (item.kind === 'delete') await deps.api(`/entries/${item.entryId}`, { method: 'DELETE' }).catch(cause => { if (!gone(cause)) throw cause })
    else await deps.api('/timer', { method: 'DELETE' }).catch(cause => { if (!gone(cause)) throw cause })
  }

  async function sync(retryFailed = false) {
    if (syncing.value || !identity.value || !navigator.onLine) return
    syncing.value = true; retrying.value = false
    const startedFor = identity.value
    let done = 0
    try {
      if (retryFailed) for (const item of failed.value) await replace({ ...item, status: 'pending', error: null })
      const tried = new Set<number>()
      for (;;) {
        if (identity.value !== startedFor) break
        const next = items.value.find(item => item.status === 'pending' && item.identity === startedFor && item.seq !== undefined && !tried.has(item.seq))
        if (!next) break
        tried.add(next.seq!)
        sendingSeq = next.seq!
        try { await send(next); await outboxRemove(next.seq!); items.value = items.value.filter(item => item.seq !== next.seq); done++ }
        catch (cause) {
          if (isOfflineError(cause) || transient(cause)) { retrying.value = true; break }
          await replace({ ...next, status: 'failed', error: { code: errorCodeOf(cause) ?? 'ERROR', status: statusOf(cause) ?? 0, dates: closedDatesOf(cause) } })
        } finally { sendingSeq = null }
      }
    } finally { syncing.value = false }
    if (done) await deps.refresh()
  }

  async function replace(item: OutboxItem) {
    const stored = await outboxStore(item)
    items.value = items.value.map(existing => existing.seq === stored.seq ? stored : existing)
  }

  async function discard(seq: number) {
    await outboxRemove(seq)
    items.value = items.value.filter(item => item.seq !== seq)
    await deps.refresh()
  }

  // --- The timer --------------------------------------------------------------------------------------

  const nowState = (timerValue: RunningTimer | null): TimerState => ({ timer: timerValue, nowUtc: new Date().toISOString() })
  const localTimer = () => safe(() => kvGet<RunningTimer>(identity.value, 'timer'), undefined)

  /** An entry for a stopped timer: its day and start are the timer's start, its end is now, in the organization's time zone. */
  function entryFromTimer(timerValue: RunningTimer, body: Record<string, unknown>): EntryBody {
    const started = zonedParts(new Date(timerValue.startedAtUtc), timeZoneId.value)
    const ended = zonedParts(new Date(), timeZoneId.value)
    const hours = (value: unknown, fallback: string) => typeof value === 'string' ? value : `${fallback}:00`
    const taskName = String(body.taskName ?? timerValue.taskName ?? '').trim()
    return { date: typeof body.date === 'string' ? body.date : started.date, startTime: hours(body.startTime, started.time), endTime: hours(body.endTime, ended.time),
      taskName, detail: (body.detail as string) ?? timerValue.detail, notes: (body.notes as string) ?? timerValue.notes,
      projectId: (body.projectId as string) ?? timerValue.projectId, categoryId: (body.categoryId as string) ?? timerValue.categoryId,
      personalTaskId: (body.personalTaskId as string) ?? timerValue.personalTaskId, includesBreak: Boolean(body.includesBreak ?? timerValue.includesBreak) }
  }

  async function stopIntoQueue(timerValue: RunningTimer, body: Record<string, unknown>, discardTimer: boolean) {
    const placed = typeof body.startTime === 'string' && typeof body.endTime === 'string'
    if (!placed && Date.now() - Date.parse(timerValue.startedAtUtc) >= 24 * 3600 * 1000) throw localError('TIMER_TOO_LONG')
    const entry = entryFromTimer(timerValue, body)
    if (entry.startTime.slice(0, 5) === entry.endTime.slice(0, 5)) throw localError('INVALID_TIME_RANGE', 400)
    await enqueue({ kind: 'create', entryId: crypto.randomUUID(), body: entry, discardTimer })
    void sync()
  }

  /** What the shared timer panel calls, in place of the page's `api`: the server's timer when online, a local one when not. */
  async function timerApi<T>(path: string, options: { method?: string; body?: object } = {}): Promise<T> {
    const method = options.method ?? 'GET'
    const body = (options.body ?? {}) as Record<string, unknown>
    const local = canQueue.value ? await localTimer() : undefined
    if (method === 'GET') {
      if (local) return nowState(local) as T
      try { return await readThrough('timer', () => deps.api<T>(path, options)) }
      // Offline with nothing saved: no timer is known to run, so one can be started on this device.
      catch (cause) { if (isOfflineError(cause) && canQueue.value) return nowState(null) as T; throw cause }
    }
    if (path === '/timer/start') {
      if (local) throw localError('TIMER_RUNNING')
      try {
        const started = await deps.api<TimerState>(path, options)
        if (identity.value) void safe(() => cachePut(identity.value, 'timer', started), undefined)
        return started as T
      } catch (cause) {
        if (!isOfflineError(cause) || !canQueue.value) throw cause
        const known = await safe(() => cacheGet<TimerState>(identity.value, 'timer'), undefined)
        if (known?.timer) throw localError('TIMER_RUNNING')
        const started: RunningTimer = { id: crypto.randomUUID(), startedAtUtc: new Date().toISOString(), taskName: typeof body.taskName === 'string' ? body.taskName : null,
          detail: null, notes: null, projectId: null, categoryId: null, personalTaskId: null, includesBreak: false }
        await kvSet(identity.value, 'timer', started)
        return nowState(started) as T
      }
    }
    if (path === '/timer/stop') {
      if (local) {
        await stopIntoQueue(local, body, false)
        await kvDelete(identity.value, 'timer')
        return undefined as T
      }
      try { return await deps.api<T>(path, options) }
      catch (cause) {
        if (!isOfflineError(cause) || !canQueue.value) throw cause
        const known = await safe(() => cacheGet<TimerState>(identity.value, 'timer'), undefined)
        if (!known?.timer) throw cause
        await stopIntoQueue(known.timer, body, true)
        void safe(() => cachePut(identity.value, 'timer', nowState(null)), undefined)
        return undefined as T
      }
    }
    if (method === 'DELETE' && local) { await kvDelete(identity.value, 'timer'); return undefined as T }
    return await deps.api<T>(path, options)
  }

  // --- Triggers ---------------------------------------------------------------------------------------

  async function reconnected() {
    online.value = true
    await sync()
    if (stale.value) await deps.refresh()
  }
  const visible = () => { if (document.visibilityState === 'visible' && pending.value.length) void sync() }
  const wentOffline = () => { online.value = false }
  function start() {
    online.value = navigator.onLine
    window.addEventListener('online', reconnected)
    window.addEventListener('offline', wentOffline)
    document.addEventListener('visibilitychange', visible)
    timer = setInterval(() => { if (pending.value.length) void sync() }, 60_000)
  }
  function stop() {
    window.removeEventListener('online', reconnected)
    window.removeEventListener('offline', wentOffline)
    document.removeEventListener('visibilitychange', visible)
    if (timer) clearInterval(timer)
  }

  return { online, identity, identityUnknown, items, pending, failed, syncing, retrying, stale, canQueue, readContext, readThrough, overlay, submit, fixAsNew, sync,
    discard, timerApi, start, stop, offlineError }
}
