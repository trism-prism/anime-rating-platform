import { useEffect, useRef, useState } from 'react'
import { api, ApiError } from '../api'
import ConfirmDialog from './ConfirmDialog'

type AdminDetail = { id: number; name: string; kind: string; entries: { id: number; name: string; position: number; format: string | null; seriesLocked: boolean }[] }

type Row = { id: number; name: string; kind: string; entries: number }

function message(err: unknown) {
  // the API sends a message with its 404s; fall back for older servers
  if (err instanceof ApiError && err.status === 404) return err.message.startsWith('Request failed') ? 'Not found: it may have been merged or removed. Search again.' : err.message
  if (err instanceof ApiError && err.status === 403) return 'Admin access is required.'
  return (err as Error).message
}

// search GET /admin/series and pick one row
function Picker({ label, value, onPick }: { label: string; value: Row | null; onPick: (r: Row) => void }) {
  const [text, setText] = useState('')
  const [res, setRes] = useState<{ q: string; list: Row[]; error: string } | null>(null)
  const q = text.trim()

  useEffect(() => {
    if (!q) return
    const run = { stale: false }
    const t = setTimeout(() => {
      api<Row[]>(`/admin/series?search=${encodeURIComponent(q)}`)
        .then((list) => !run.stale && setRes({ q, list, error: '' }))
        .catch((e) => !run.stale && setRes({ q, list: [], error: message(e) }))
    }, 250)
    return () => {
      run.stale = true
      clearTimeout(t)
    }
  }, [q])

  const ready = q !== '' && res?.q === q

  return (
    <div className="picker">
      <input aria-label={`${label}: search series`} placeholder={`${label}: search series`} value={text} onChange={(e) => setText(e.target.value)} />
      <div className="picker-list" role="group" aria-label={`${label} results`}>
        {!q && <p className="muted">Type a name to search.</p>}
        {q && !ready && <p className="muted">Searching...</p>}
        {ready && res.error && <p className="error">{res.error}</p>}
        {ready && !res.error && res.list.length === 0 && <p className="muted">No series found.</p>}
        {ready && res.list.map((r) => (
          <button key={r.id} type="button" aria-pressed={value?.id === r.id} onClick={() => onPick(r)}>
            <span>{r.name}</span>
            <span className="muted">{r.kind} · {r.entries}</span>
          </button>
        ))}
      </div>
      <div className="muted">{value ? `Selected: ${value.name} (${value.kind}, ${value.entries} entries)` : 'Nothing selected'}</div>
    </div>
  )
}

type Status = { ok: boolean; text: string } | null

function StatusLine({ status }: { status: Status }) {
  return <p className={`admin-status ${status?.ok ? 'muted' : 'error'}`} role="status">{status?.text ?? ''}</p>
}

function MergeTool() {
  const [gen, setGen] = useState(0) // bump to clear both pickers
  const [from, setFrom] = useState<Row | null>(null)
  const [into, setInto] = useState<Row | null>(null)
  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState(false)
  const [status, setStatus] = useState<Status>(null)

  const problem = from && into ? (from.id === into.id ? 'Pick two different series.' : from.kind !== into.kind ? 'Anime and manga never share a series.' : '') : ''

  async function merge() {
    if (!from || !into) return
    setBusy(true)
    try {
      await api('/admin/series/merge', { method: 'POST', body: JSON.stringify({ fromId: from.id, intoId: into.id }) })
      setStatus({ ok: true, text: `Merged "${from.name}" into "${into.name}". Its ${from.entries} titles are now locked to it.` })
      setFrom(null)
      setInto(null)
      setGen((g) => g + 1)
    } catch (e) {
      setStatus({ ok: false, text: `Merge failed: ${message(e)}` })
    } finally {
      setBusy(false)
      setConfirming(false)
    }
  }

  return (
    <div className="admin-box">
      <h3>Merge two series</h3>
      <div className="row" style={{ alignItems: 'flex-start' }} key={gen}>
        <Picker label="From (will be removed)" value={from} onPick={setFrom} />
        <Picker label="Into (kept)" value={into} onPick={setInto} />
      </div>
      <div className="row" style={{ marginTop: 8 }}>
        <button disabled={!from || !into || !!problem || busy} onClick={() => setConfirming(true)}>Merge...</button>
        {problem && <span className="error">{problem}</span>}
      </div>
      <StatusLine status={status} />
      <ConfirmDialog open={confirming} title="Merge series?" confirmLabel="Merge" busy={busy} onConfirm={merge} onCancel={() => setConfirming(false)}>
        <p>All {from?.entries} titles of <strong>{from?.name}</strong> move into <strong>{into?.name}</strong>, and {from?.name} is deleted. The moved titles are locked so the importer will not undo this. There is no undo; reversing it means moving titles back one by one.</p>
      </ConfirmDialog>
    </div>
  )
}

function MoveTool() {
  const [gen, setGen] = useState(0)
  const [source, setSource] = useState<Row | null>(null)
  const [dest, setDest] = useState<Row | null>(null)
  const [detail, setDetail] = useState<{ id: number; series?: AdminDetail; error?: string } | null>(null)
  const selected = useRef<number | null>(null) // the series the table should show; older responses are dropped
  const [busy, setBusy] = useState<number | null>(null)
  const [status, setStatus] = useState<Status>(null)

  function load(id: number) {
    api<AdminDetail>(`/admin/series/${id}`)
      .then((series) => selected.current === id && setDetail({ id, series }))
      .catch((e) => selected.current === id && setDetail({ id, error: message(e) }))
  }

  useEffect(() => {
    selected.current = source?.id ?? null
    if (source) load(source.id)
  }, [source])

  async function move(entryId: number, name: string, target: Row | null) {
    setBusy(entryId)
    try {
      const r = await api<{ seriesId: number }>(`/admin/titles/${entryId}/move`, { method: 'POST', body: JSON.stringify({ seriesId: target?.id ?? null }) })
      setStatus({ ok: true, text: target ? `Moved "${name}" to "${target.name}" and locked it.` : `Split "${name}" into a new series (id ${r.seriesId}) and locked it.` })
      if (selected.current !== null) load(selected.current) // the series may have been renamed or removed
    } catch (e) {
      setStatus({ ok: false, text: `Move failed: ${message(e)}` })
    } finally {
      setBusy(null)
    }
  }

  const series = detail && source && detail.id === source.id ? detail : null

  return (
    <div className="admin-box">
      <h3>Move or split a title</h3>
      <div className="row" style={{ alignItems: 'flex-start' }} key={gen}>
        <Picker label="Series" value={source} onPick={(r) => { setSource(r); setStatus(null) }} />
        <Picker label="Move into" value={dest} onPick={setDest} />
      </div>
      <div style={{ minHeight: 120, marginTop: 8 }}>
        {source && !series && <p className="muted">Loading titles...</p>}
        {series?.error && <p className="error">{series.error}</p>}
        {series?.series && (
          <table className="table">
            <thead><tr><th>#</th><th>Title</th><th>State</th><th /></tr></thead>
            <tbody>
              {series.series.entries.map((e) => {
                const blocked = !dest ? 'Pick a destination series' : dest.id === series.id ? 'Already in that series' : dest.kind !== source?.kind ? 'Anime and manga never share a series' : ''
                return (
                  <tr key={e.id}>
                    <td>{e.position}</td>
                    <td>{e.name}</td>
                    <td className="muted">{e.seriesLocked ? 'Locked' : 'Importer-managed'}</td>
                    <td>
                      <div className="row">
                        <button disabled={busy !== null || !!blocked} title={blocked} onClick={() => move(e.id, e.name, dest)}>Move to destination</button>
                        <button disabled={busy !== null} onClick={() => move(e.id, e.name, null)}>Split into new series</button>
                      </div>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        )}
      </div>
      <StatusLine status={status} />
      {source && <button onClick={() => { setSource(null); setDest(null); setDetail(null); setGen((g) => g + 1) }}>Clear</button>}
    </div>
  )
}

export default function AdminSeries() {
  return (
    <>
      <h2>Series tools</h2>
      <p className="muted">Fixes for what the importer got wrong. Moved titles are locked, so the next import leaves them alone.</p>
      <MergeTool />
      <MoveTool />
    </>
  )
}
