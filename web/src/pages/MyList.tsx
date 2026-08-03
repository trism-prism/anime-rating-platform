import { useEffect, useMemo, useRef, useState } from 'react'
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router-dom'
import { api } from '../api'
import { useAuth } from '../auth'
import AudienceSelect from '../components/AudienceSelect'
import ConfirmDialog from '../components/ConfirmDialog'
import CreateListDialog from '../components/CreateListDialog'
import MyListRow, { type ListRow } from '../components/MyListRow'
import Select from '../components/Select'
import { plural } from '../format'
import { STATUSES, type Audience, type ListSummary, type MyRating, type WatchEntry, type WatchStatus } from '../types'
import { useTitle } from '../useTitle'

const PAGE = 30 // rows shown before "Show more"

// the chips; "Unlisted" is a title you rated that is not on your list
type Filter = 'All' | WatchStatus | 'Unlisted'
const FILTERS: { value: Filter; label: string }[] = [
  { value: 'All', label: 'All' },
  ...STATUSES.map((s) => ({ value: s, label: s })),
  { value: 'Unlisted', label: 'Not on list' },
]
const SORTS = [
  { value: 'recent', label: 'Recently rated' },
  { value: 'title', label: 'Title' },
  { value: 'score', label: 'Score' },
]

// one row per title: list entry, its rating and its volume ratings joined by title
function buildRows(list: WatchEntry[], ratings: MyRating[]): ListRow[] {
  const rows = new Map<number, ListRow>()
  for (const e of list) {
    rows.set(e.titleId, { titleId: e.titleId, seriesId: e.seriesId, name: e.name, seriesName: e.seriesName !== e.name ? e.seriesName : null, coverUrl: e.coverUrl, entry: e, rating: null, volumes: [] })
  }
  for (const r of ratings) {
    let row = rows.get(r.titleId)
    if (!row) {
      row = { titleId: r.titleId, seriesId: r.seriesId, name: r.titleName, seriesName: null, coverUrl: r.coverUrl, entry: null, rating: null, volumes: [] }
      rows.set(r.titleId, row)
    }
    if (r.volume == null) row.rating = r
    else row.volumes.push(r)
  }
  for (const row of rows.values()) row.volumes.sort((a, b) => (a.volume ?? 0) - (b.volume ?? 0))
  return [...rows.values()]
}

const statusOf = (r: ListRow): Filter => r.entry?.status ?? 'Unlisted'
const ratedAt = (r: ListRow) => [r.rating?.createdAt, ...r.volumes.map((v) => v.createdAt)].filter(Boolean).sort().pop() ?? ''
const byName = (a: ListRow, b: ListRow) => a.name.localeCompare(b.name)

export default function MyList() {
  useTitle('My List')
  const { user } = useAuth()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const [lists, setLists] = useState<ListSummary[] | null>(null)
  const [listsError, setListsError] = useState('')
  const [creating, setCreating] = useState(false)
  const [deleting, setDeleting] = useState<ListSummary | null>(null) // list awaiting delete confirmation
  const [deleteBusy, setDeleteBusy] = useState(false)
  const [removing, setRemoving] = useState<ListRow | null>(null) // title awaiting remove confirmation
  const [removeBusy, setRemoveBusy] = useState(false)
  const [list, setList] = useState<WatchEntry[] | null>(null)
  const [ratings, setRatings] = useState<MyRating[] | null>(null)
  const [error, setError] = useState('')
  const [rowErrors, setRowErrors] = useState<Record<number, string>>({})
  const [listError, setListError] = useState('')
  const [ratingsError, setRatingsError] = useState('')
  const [tick, setTick] = useState(0) // bump to retry loading
  const [limit, setLimit] = useState(PAGE)
  const [pinned, setPinned] = useState<Set<number>>(new Set()) // changed rows stay in the view until the filters change
  const inFlight = useRef(new Set<string>()) // changes being saved, readable at once (state lags a fast double change)
  const pending = useRef(new Map<number, WatchStatus>()) // latest status picked per row and not yet confirmed; laid over any list loaded meanwhile
  const focusTimer = useRef<number | undefined>(undefined)
  const withPending = (l: WatchEntry[]) => l.map((x) => (pending.current.has(x.titleId) ? { ...x, status: pending.current.get(x.titleId)! } : x))
  const [changing, setChanging] = useState<string[]>([])
  const listRef = useRef<HTMLUListElement>(null)
  const searchRef = useRef<HTMLInputElement>(null)
  const focusFrom = useRef<number | null>(null)

  useEffect(() => {
    if (!user) return
    api<WatchEntry[]>('/me/watchlist').then((l) => { setList(withPending(l)); setListError('') }).catch((e: Error) => setListError(e.message))
    api<ListSummary[]>('/me/lists').then((l) => { setLists(l); setListsError('') }).catch((e: Error) => setListsError(e.message))
    api<MyRating[]>('/me/ratings').then((r) => { setRatings(r); setRatingsError('') }).catch((e: Error) => setRatingsError(e.message))
  }, [user, tick])

  // filters live in the URL, so back and refresh keep them
  const status = FILTERS.find((f) => f.value.toLowerCase() === params.get('status'))?.value ?? 'All'
  const query = params.get('q') ?? ''
  const sort = SORTS.find((s) => s.value === params.get('sort'))?.value ?? 'recent'

  function setFilters(next: { status?: Filter; q?: string; sort?: string }) {
    const p = new URLSearchParams(params)
    const set = (key: string, value: string, fallback: string) => (value === fallback ? p.delete(key) : p.set(key, value))
    if (next.status !== undefined) set('status', next.status.toLowerCase(), 'all')
    if (next.q !== undefined) set('q', next.q, '')
    if (next.sort !== undefined) set('sort', next.sort, 'recent')
    setParams(p, { replace: true })
    setLimit(PAGE)
    setPinned(new Set())
  }

  const rows = useMemo(() => buildRows(list ?? [], ratings ?? []), [list, ratings])
  const q = query.trim().toLowerCase()
  const matches = rows.filter((r) => !q || r.name.toLowerCase().includes(q) || (r.seriesName ?? '').toLowerCase().includes(q))
  const visible = matches.filter((r) => status === 'All' || statusOf(r) === status || pinned.has(r.titleId)).sort((a, b) => {
    if (sort === 'title') return byName(a, b)
    if (sort === 'score') return (b.rating?.overall ?? -1) - (a.rating?.overall ?? -1) || byName(a, b)
    return ratedAt(b).localeCompare(ratedAt(a)) || byName(a, b)
  })
  const shown = visible.slice(0, limit)
  // the chip you are on counts what the list shows (rows you just moved included), so it agrees with "Showing X of Y"
  const count = (f: Filter) => (f === status ? visible.length : f === 'All' ? matches.length : matches.filter((r) => statusOf(r) === f).length)

  // after "Show more", focus the first new row, so the keyboard does not start over at the top
  useEffect(() => {
    if (focusFrom.current == null) return
    listRef.current?.children[focusFrom.current]?.querySelector<HTMLElement>('a')?.focus()
    focusFrom.current = null
  }, [limit])

  useEffect(() => () => window.clearTimeout(focusTimer.current), [])

  if (!user) return <Navigate to="/login" replace />

  const setRowError = (id: number, message: string) => setRowErrors((r) => {
    const n = { ...r }
    if (message) n[id] = message
    else delete n[id]
    return n
  })
  const pin = (id: number) => setPinned((p) => new Set(p).add(id))
  const begin = (key: string) => { inFlight.current.add(key); setChanging([...inFlight.current]) }
  const end = (key: string) => { inFlight.current.delete(key); setChanging([...inFlight.current]) }
  const busy = (key: string) => changing.includes(key)
  // picks still waiting for the server stay laid over the loaded list, so another row's reload cannot undo them
  const reloadList = () => api<WatchEntry[]>('/me/watchlist').then((l) => setList(withPending(l)))
    .catch((e: Error) => setError(`Your list could not be refreshed: ${e.message}`))

  // optimistic: the row changes at once; one request per row at a time, the latest pick made meanwhile is sent after it;
  // on failure the list is loaded again, so the select shows what the server has
  async function changeStatus(row: ListRow, next: WatchStatus) {
    const id = row.titleId
    const key = `s-${id}`
    setRowError(id, '')
    pin(id)
    pending.current.set(id, next)
    if (row.entry) setList((l) => l && l.map((x) => (x.titleId === id ? { ...x, status: next } : x)))
    if (inFlight.current.has(key)) return
    begin(key)
    let failed = false
    try {
      let sent: WatchStatus | undefined
      while (pending.current.get(id) !== sent) {
        sent = pending.current.get(id)!
        await api(`/me/watchlist/${id}`, { method: 'PUT', body: JSON.stringify({ status: sent, progress: row.entry?.progress ?? 0 }) })
        // confirmed by the server; a newer pick keeps its own value until it is confirmed too
        if (row.entry && pending.current.get(id) === sent) setList((l) => l && l.map((x) => (x.titleId === id ? { ...x, status: sent! } : x)))
      }
    } catch (err) {
      failed = true
      setRowError(id, (err as Error).message)
    } finally {
      pending.current.delete(id)
      end(key) // before the reload, so a pick made while it runs is not ignored
    }
    // a new entry needs the list's cover, series and type; a failure needs what the server has
    if (failed || !row.entry) await reloadList()
  }

  // one change at a time per row; on failure the lists are loaded again, so the select shows what the server has
  async function changeAudience(key: string, path: string, audience: Audience, apply: () => void, fail: (message: string) => void, extra: object = {}) {
    if (inFlight.current.has(key)) return
    begin(key)
    try {
      await api(path, { method: 'PUT', body: JSON.stringify({ ...extra, audience }) })
      apply()
    } catch (err) {
      fail((err as Error).message)
      setTick((t) => t + 1)
    } finally {
      end(key)
    }
  }

  async function remove() {
    const row = removing
    if (!row) return
    const at = visible.findIndex((r) => r.titleId === row.titleId)
    const neighbours = [visible[at + 1], visible[at - 1]].filter(Boolean).map((r) => r.titleId)
    setRowError(row.titleId, '')
    setRemoveBusy(true)
    try {
      await api(`/me/watchlist/${row.titleId}`, { method: 'DELETE' })
      pin(row.titleId)
      setList((l) => l && l.filter((x) => x.titleId !== row.titleId))
    } catch (err) {
      setRowError(row.titleId, (err as Error).message)
    } finally {
      setRemoveBusy(false)
      setRemoving(null)
    }
    // the dialog gives focus back to a button that may be gone: put it on the row, or its neighbour
    window.clearTimeout(focusTimer.current)
    focusTimer.current = window.setTimeout(() => {
      for (const id of [row.titleId, ...neighbours]) {
        const link = document.querySelector<HTMLElement>(`[data-row="${id}"] a`)
        if (link) return link.focus()
      }
      searchRef.current?.focus()
    }, 60)
  }

  async function removeList() {
    const l = deleting
    if (!l) return
    setError('')
    setDeleteBusy(true)
    try {
      await api(`/me/lists/${l.id}`, { method: 'DELETE' })
      setLists((all) => all && all.filter((x) => x.id !== l.id))
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setDeleteBusy(false)
      setDeleting(null)
    }
  }

  const ready = list !== null && (ratings !== null || ratingsError !== '')

  return (
    <div>
      <h1>My List</h1>
      {error && <p className="error" role="alert">{error}</p>}
      {!list && listError && <p className="error">{listError} <button onClick={() => { setListError(''); setTick((t) => t + 1) }}>Retry</button></p>}
      {((!list && !listError) || (list && !ratings && !ratingsError)) && <p className="muted">Loading</p>}
      {list && !ratings && ratingsError && <p className="error">Your ratings did not load: {ratingsError} <button onClick={() => { setRatingsError(''); setTick((t) => t + 1) }}>Retry</button></p>}
      {ready && rows.length === 0 && (
        <div className="mylist-empty">
          <p className="muted">Nothing on your list yet. Add anime and manga from Browse.</p>
          <Link to="/" className="button">Browse titles</Link>
        </div>
      )}
      {ready && rows.length > 0 && (
        <>
          <div className="mylist-tools">
            <input ref={searchRef} type="search" className="mylist-search" aria-label="Search your list" placeholder="Search your list" value={query}
              onChange={(e) => setFilters({ q: e.target.value })} />
            <Select label="Sort by" value={sort} options={SORTS} onChange={(v) => setFilters({ sort: v })} />
          </div>
          <div className="mylist-chips" role="group" aria-label="Filter by status">
            {FILTERS.filter((f) => f.value !== 'Unlisted' || count('Unlisted') > 0 || status === 'Unlisted').map((f) => (
              <button key={f.value} type="button" className={status === f.value ? 'on' : ''} aria-pressed={status === f.value} onClick={() => setFilters({ status: f.value })}>
                {f.label} <span className="num count">{count(f.value)}</span>
              </button>
            ))}
          </div>
          {visible.length === 0 && (
            <p className="muted">No titles match. <button type="button" className="link-button" onClick={() => setFilters({ status: 'All', q: '' })}>Clear filters</button></p>
          )}
          <ul className="mylist" ref={listRef}>
            {shown.map((r) => (
              <MyListRow key={r.titleId} row={r} busy={busy} error={rowErrors[r.titleId] ?? ''}
                onStatus={changeStatus}
                onEntryAudience={(e, a) => changeAudience(`w-${e.titleId}`, `/me/watchlist/${e.titleId}/audience`, a,
                  () => setList((l) => l && l.map((x) => (x.titleId === e.titleId ? { ...x, audience: a } : x))), (m) => setRowError(e.titleId, m))}
                onRatingAudience={(rt, a) => changeAudience(`r-${rt.titleId}-${rt.volume ?? 'all'}`,
                  `/titles/${rt.titleId}/rating/audience${rt.volume == null ? '' : `?volume=${rt.volume}`}`, a,
                  () => setRatings((all) => all && all.map((x) => (x.titleId === rt.titleId && x.volume === rt.volume ? { ...x, audience: a } : x))), (m) => setRowError(rt.titleId, m))}
                onDates={(id, startedOn, finishedOn) => setList((l) => l && l.map((x) => (x.titleId === id ? { ...x, startedOn, finishedOn } : x)))}
                onRemove={setRemoving} />
            ))}
          </ul>
          {visible.length > PAGE && (
            <div className="mylist-more">
              <p className="muted" role="status">Showing <span className="num">{shown.length}</span> of <span className="num">{visible.length}</span></p>
              {shown.length < visible.length && (
                <button type="button" onClick={() => { focusFrom.current = shown.length; setLimit(shown.length + PAGE) }}>Show more</button>
              )}
            </div>
          )}
        </>
      )}

      <ConfirmDialog open={removing !== null} title={`Remove "${removing?.name ?? ''}" from your list?`} confirmLabel="Remove" busy={removeBusy}
        onConfirm={remove} onCancel={() => setRemoving(null)}>
        <p>Its status and dates are cleared. Your rating stays.</p>
      </ConfirmDialog>

      <div className="row reviews-head">
        <h2>Custom lists</h2>
        <button onClick={() => setCreating(true)}>Create list</button>
      </div>
      {!lists && listsError && <p className="error">{listsError} <button onClick={() => { setListsError(''); setTick((t) => t + 1) }}>Retry</button></p>}
      {!lists && !listsError && <p className="muted">Loading</p>}
      {lists && lists.length === 0 && <p className="muted">No lists yet.</p>}
      {lists?.map((l) => (
        <div key={l.id} className="list-row">
          <div className="mylist-list-name">
            <Link to={`/lists/${l.id}`} className="list-name">{l.name}</Link>
            <span className="muted">{plural(l.itemCount, 'item')}</span>
          </div>
          <AudienceSelect label={`Visibility of ${l.name}`} value={l.audience ?? 'Public'} disabled={changing.includes(`l-${l.id}`)}
            onChange={(a) => changeAudience(`l-${l.id}`, `/me/lists/${l.id}`, a, () => setLists((all) => all && all.map((x) => (x.id === l.id ? { ...x, audience: a } : x))), setError, { name: l.name })} />
          <button onClick={() => setDeleting(l)}>Delete</button>
        </div>
      ))}
      <ConfirmDialog open={deleting !== null} title={`Delete "${deleting?.name ?? ''}"?`} confirmLabel="Delete list" busy={deleteBusy}
        onConfirm={removeList} onCancel={() => setDeleting(null)}>
        <p>The list and its {plural(deleting?.itemCount ?? 0, 'item')} are removed. The titles themselves stay in the catalogue.</p>
      </ConfirmDialog>
      <CreateListDialog open={creating} onClose={() => setCreating(false)} onCreated={(l) => { setCreating(false); navigate(`/lists/${l.id}`) }} />
    </div>
  )
}
