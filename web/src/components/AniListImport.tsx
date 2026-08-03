import { useCallback, useEffect, useId, useState } from 'react'
import { api } from '../api'
import type { ListImportStatus } from '../types'

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`

// import a public AniList list; it runs on the server, so this polls until it is done
export default function AniListImport() {
  const [name, setName] = useState('')
  const [status, setStatus] = useState<ListImportStatus | null>(null)
  const [error, setError] = useState('')
  const [polling, setPolling] = useState(true)
  const id = useId()

  const load = useCallback(() => api<ListImportStatus>('/me/import/anilist').then((s) => { setStatus(s); setPolling(s.running) }).catch(() => setPolling(false)), [])

  useEffect(() => { load() }, [load])
  useEffect(() => {
    if (!polling) return
    const t = setInterval(load, 3000)
    return () => clearInterval(t)
  }, [polling, load])

  async function start(e: React.FormEvent) {
    e.preventDefault()
    setError('')
    try {
      const s = await api<ListImportStatus>('/me/import/anilist', { method: 'POST', body: JSON.stringify({ userName: name }) })
      setStatus(s)
      setPolling(true)
    } catch (err) {
      setError((err as Error).message)
    }
  }

  const r = status?.result
  return (
    <>
      <h2 id="h-import">Import from AniList</h2>
      <p className="card-desc">Import your public AniList list by username.</p>
      <form className="settings-form" onSubmit={start}>
        <label htmlFor={id}>AniList username</label>
        <div className="row import-row">
          <input id={id} value={name} maxLength={20} autoComplete="off" onChange={(e) => setName(e.target.value)} />
          <button type="submit" className="btn-secondary" disabled={!name.trim() || status?.running}>Import</button>
        </div>
      </form>
      {status?.running && <p className="muted" role="status">Importing</p>}
      {error && <p className="error" role="alert">{error}</p>}
      {status?.error && <p className="error" role="alert">{status.error}</p>}
      {r && !status?.running && (
        <p className="import-result" role="status">
          Added {plural(r.added, 'title', 'titles')} to My List, only visible to you. {r.alreadyOnList} {r.alreadyOnList === 1 ? 'was' : 'were'} already there
          and {r.unmatched} {r.unmatched === 1 ? 'was' : 'were'} not found here. Scores are not imported.
        </p>
      )}
    </>
  )
}
