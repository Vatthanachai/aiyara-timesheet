/**
 * The Timesheet remote's local store and outbox rules. See the Mobile And Offline Plan.
 *
 * Everything lives in one IndexedDB database on this remote's origin, and every row carries the identity
 * (`tenantId:userId`) it belongs to. No password or token is ever written here.
 */
import type { Entry, WorkHours } from './timesheet'
import { workedMinutes } from './timesheet'

/** What `POST /entries` and `PUT /entries/{id}` take, with times as HH:mm:ss. */
export type EntryBody = { date: string; startTime: string; endTime: string; taskName: string; detail: string | null; notes: string | null
  projectId: string | null; categoryId: string | null; personalTaskId: string | null; includesBreak: boolean }
/** Why the server refused a queued item: its error code, HTTP status, and the days a closed month named. */
export type ItemError = { code: string; status: number; dates: string[] }
/** Enough of an entry to name it in the list of waiting changes. */
export type ItemSummary = { taskName: string; date: string; startTime: string; endTime: string }
export type OutboxItem = {
  seq?: number
  identity: string
  /** The entry this item changes; for a create, the id the client chose and the server will keep. */
  entryId: string
  /** `timer-discard` only throws the running timer away (a stopped-offline entry was deleted before it synced). */
  kind: 'create' | 'update' | 'delete' | 'timer-discard'
  body: EntryBody | null
  /** A create made by stopping a server-kept timer offline also discards that timer once the entry is accepted. */
  discardTimer: boolean
  summary: ItemSummary
  status: 'pending' | 'failed'
  error: ItemError | null
  createdAt: string
}
export type Change =
  | { kind: 'create' | 'update'; entryId: string; body: EntryBody; discardTimer?: boolean }
  | { kind: 'delete'; entryId: string; summary: ItemSummary }

/** What storing a change does to the queue: one item written, one removed, or nothing. */
export type Plan = { put?: OutboxItem; remove?: number }

const summaryOf = (body: EntryBody): ItemSummary => ({ taskName: body.taskName, date: body.date, startTime: body.startTime, endTime: body.endTime })

/**
 * Folds a change into the queue so it stays small and in order (decision 7 of the plan). An item that is being
 * sent (`sendingSeq`) is never rewritten: a later change to the same entry is queued after it.
 */
export function planChange(items: OutboxItem[], change: Change, identity: string, sendingSeq: number | null, now = new Date().toISOString()): Plan {
  const existing = [...items].reverse().find(item => item.entryId === change.entryId && item.seq !== sendingSeq && item.kind !== 'timer-discard')
  const fresh = (item: Partial<OutboxItem> & Pick<OutboxItem, 'kind' | 'body' | 'summary'>): OutboxItem =>
    ({ identity, entryId: change.entryId, discardTimer: false, status: 'pending', error: null, createdAt: now, ...item })
  const reset = (item: OutboxItem, patch: Partial<OutboxItem>): OutboxItem => ({ ...item, status: 'pending', error: null, ...patch })

  if (change.kind === 'delete') {
    if (!existing) return { put: fresh({ kind: 'delete', body: null, summary: change.summary }) }
    if (existing.kind === 'create')
      // The server never heard of it. A create that also discards the timer leaves that one job behind.
      return existing.discardTimer && existing.seq !== undefined
        ? { put: reset(existing, { kind: 'timer-discard', body: null, discardTimer: true }) }
        : { remove: existing.seq }
    if (existing.kind === 'delete') return {}
    return { put: reset(existing, { kind: 'delete', body: null, summary: change.summary }) }
  }
  if (existing?.kind === 'create') return { put: reset(existing, { body: change.body, summary: summaryOf(change.body) }) }
  if (existing?.kind === 'update' && change.kind === 'update') return { put: reset(existing, { body: change.body, summary: summaryOf(change.body) }) }
  return { put: fresh({ kind: change.kind, body: change.body, summary: summaryOf(change.body), discardTimer: change.kind === 'create' && Boolean(change.discardTimer) }) }
}

/** The entry a queued create or update stands for, with an estimated duration the server replaces on sync. */
export function entryOf(item: OutboxItem, hours: WorkHours): Entry {
  const body = item.body!
  return { id: item.entryId, date: body.date, startTime: body.startTime, endTime: body.endTime,
    durationMinutes: workedMinutes({ startTime: body.startTime.slice(0, 5), endTime: body.endTime.slice(0, 5), includesBreak: body.includesBreak }, hours),
    taskName: body.taskName, detail: body.detail, notes: body.notes, projectId: body.projectId, categoryId: body.categoryId,
    personalTaskId: body.personalTaskId, includesBreak: body.includesBreak, sync: item.status }
}

/** The month's entries as the member sees them now: the server's list with the waiting changes laid over it. */
export function withQueue(entries: Entry[], items: OutboxItem[], month: string, hours: WorkHours): Entry[] {
  let result = [...entries]
  for (const item of items) {
    if (item.kind === 'delete') {
      // A pending delete hides the entry; one the server refused leaves it in place, marked.
      result = item.status === 'pending' ? result.filter(entry => entry.id !== item.entryId)
        : result.map(entry => entry.id === item.entryId ? { ...entry, sync: 'failed' as const } : entry)
    } else if (item.kind === 'create' || item.kind === 'update') {
      const stand = entryOf(item, hours)
      const without = result.filter(entry => entry.id !== item.entryId)
      result = stand.date.startsWith(month) ? [...without, stand] : without
    }
  }
  return result
}

// --- IndexedDB -------------------------------------------------------------------------------------------

const databaseName = 'aiyara-timesheet-offline'
const cacheMaxAgeMs = 30 * 24 * 3600 * 1000
let opened: Promise<IDBDatabase> | null = null

function open(): Promise<IDBDatabase> {
  opened ??= new Promise((resolve, reject) => {
    const request = indexedDB.open(databaseName, 1)
    request.onupgradeneeded = () => {
      const db = request.result
      const outbox = db.createObjectStore('outbox', { keyPath: 'seq', autoIncrement: true })
      outbox.createIndex('identity', 'identity')
      db.createObjectStore('cache', { keyPath: 'key' })
      db.createObjectStore('kv', { keyPath: 'key' })
    }
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => { opened = null; reject(request.error) }
  })
  return opened
}

async function run<T>(store: string, mode: IDBTransactionMode, work: (store: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  const db = await open()
  return new Promise<T>((resolve, reject) => {
    const transaction = db.transaction(store, mode)
    const request = work(transaction.objectStore(store))
    transaction.oncomplete = () => resolve(request.result)
    transaction.onerror = () => reject(transaction.error)
    transaction.onabort = () => reject(transaction.error)
  })
}

/** Whether this browser can keep a local store; without it the page works online as before. */
export const offlineSupported = () => typeof indexedDB !== 'undefined'

type CacheRow<T> = { key: string; identity: string; value: T; savedAt: number }

export async function cacheGet<T>(identity: string, name: string): Promise<T | undefined> {
  const row = await run<CacheRow<T> | undefined>('cache', 'readonly', store => store.get(`${identity}|${name}`))
  return row?.value
}
export async function cachePut<T>(identity: string, name: string, value: T) {
  await run('cache', 'readwrite', store => store.put({ key: `${identity}|${name}`, identity, value, savedAt: Date.now() } satisfies CacheRow<T>))
}
export const kvGet = <T>(identity: string, name: string) =>
  run<{ value: T } | undefined>('kv', 'readonly', store => store.get(`${identity}|${name}`)).then(row => row?.value)
export const kvSet = <T>(identity: string, name: string, value: T) =>
  run('kv', 'readwrite', store => store.put({ key: `${identity}|${name}`, identity, value })).then(() => undefined)
export const kvDelete = (identity: string, name: string) =>
  run('kv', 'readwrite', store => store.delete(`${identity}|${name}`)).then(() => undefined)

export async function outboxList(identity: string): Promise<OutboxItem[]> {
  const rows = await run<OutboxItem[]>('outbox', 'readonly', store => store.index('identity').getAll(identity))
  return rows.sort((a, b) => (a.seq ?? 0) - (b.seq ?? 0))
}
export async function outboxStore(item: OutboxItem): Promise<OutboxItem> {
  // The page's copy is reactive, and IndexedDB cannot clone a proxy: store (and return) a plain copy.
  const plain: OutboxItem = JSON.parse(JSON.stringify(item))
  const seq = await run<IDBValidKey>('outbox', 'readwrite', store => store.put(plain))
  return { ...plain, seq: Number(seq) }
}
export const outboxRemove = (seq: number) => run('outbox', 'readwrite', store => store.delete(seq)).then(() => undefined)

/** Deletes every row not belonging to `keep` (all rows when null) and, when opening, cached reads past their age. */
export async function purge(keep: string | null) {
  const db = await open()
  await new Promise<void>((resolve, reject) => {
    const transaction = db.transaction(['outbox', 'cache', 'kv'], 'readwrite')
    for (const name of ['outbox', 'cache', 'kv'] as const) {
      const store = transaction.objectStore(name)
      if (keep === null) { store.clear(); continue }
      store.openCursor().onsuccess = event => {
        const cursor = (event.target as IDBRequest<IDBCursorWithValue | null>).result
        if (!cursor) return
        if (cursor.value.identity !== keep) cursor.delete()
        else if (name === 'cache' && Date.now() - cursor.value.savedAt > cacheMaxAgeMs) cursor.delete()
        cursor.continue()
      }
    }
    transaction.oncomplete = () => resolve()
    transaction.onerror = () => reject(transaction.error)
    transaction.onabort = () => reject(transaction.error)
  })
}

// --- Failures and the clock ---------------------------------------------------------------------------------

/** The error the page's `api` throws when the request got no answer at all: the network, not the server. */
export const offlineError = () => Object.assign(new Error('offline'), { data: { statusCode: 0, errorCode: 'OFFLINE' } })
export const isOfflineError = (cause: unknown) => (cause as { data?: { errorCode?: string } } | null)?.data?.errorCode === 'OFFLINE'
/** A request that failed with no response (offline, DNS, a timeout), as opposed to an answer from the server. */
export function isNetworkFailure(cause: unknown) {
  const candidate = cause as { name?: string; response?: unknown; status?: number; statusCode?: number; data?: unknown } | null
  return candidate?.name === 'FetchError' && !candidate.response && !candidate.status && !candidate.statusCode && !candidate.data
}
/** An error shaped like the API's, for refusals the page can decide without asking the server. */
export const localError = (errorCode: string, status = 409) => Object.assign(new Error(errorCode), { data: { statusCode: status, errorCode } })

/** The date and clock time of an instant in the organization's time zone (the device's when it is unknown). */
export function zonedParts(instant: Date, timeZone?: string) {
  let parts: Record<string, string>
  try {
    parts = Object.fromEntries(new Intl.DateTimeFormat('en-GB', { timeZone: timeZone || undefined, year: 'numeric', month: '2-digit', day: '2-digit',
      hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).formatToParts(instant).map(part => [part.type, part.value]))
  } catch { return zonedParts(instant) }
  return { date: `${parts.year}-${parts.month}-${parts.day}`, time: `${parts.hour}:${parts.minute}` }
}
