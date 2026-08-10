import { useEffect, useId, useRef, useState, type ChangeEvent } from 'react'
import { api } from '../api'
import { refreshAvatars } from '../avatarVersion'
import Avatar from './Avatar'
import ConfirmDialog from './ConfirmDialog'
import { Pencil } from './InlineEdit'

const TYPES = ['image/png', 'image/jpeg', 'image/webp']
const MAX_FILE = 2 * 1024 * 1024
const SIZE = 256

// center-crop to a square and scale to 256x256, so what is stored is small whatever was chosen
async function square(file: File): Promise<Blob> {
  const img = await createImageBitmap(file).catch(() => { throw new Error("That file can't be used as a picture. Try a JPG or PNG.") })
  try {
    const side = Math.min(img.width, img.height)
    const canvas = document.createElement('canvas')
    canvas.width = canvas.height = SIZE
    canvas.getContext('2d')!.drawImage(img, (img.width - side) / 2, (img.height - side) / 2, side, side, 0, 0, SIZE, SIZE)
    // webp, or png where the browser can't write webp; the API looks at the bytes either way
    return new Promise((res, rej) => canvas.toBlob((b) => (b ? res(b) : rej(new Error("That file can't be used as a picture. Try a JPG or PNG."))), 'image/webp', 0.9))
  } finally {
    img.close()
  }
}

// your own picture, large; the pencil (on hover or focus) opens a dialog to choose, preview, save or remove it
export default function AvatarEditor({ userName, hasAvatar, onChange }: { userName: string; hasAvatar: boolean; onChange: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null)
  const file = useRef<HTMLInputElement>(null)
  const pencil = useRef<HTMLButtonElement>(null)
  const removed = useRef(false)
  const title = useId()
  const latest = useRef(0) // the newest pick, so a slow crop of an earlier file can't overwrite it
  const inputId = useId()
  const [name, setName] = useState('')
  const [blob, setBlob] = useState<Blob | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [confirmRemove, setConfirmRemove] = useState(false)
  const [preview, setPreview] = useState('')

  function clear() {
    setBlob(null)
    setName('')
    setPreview((u) => { if (u) URL.revokeObjectURL(u); return '' })
  }

  function reset() {
    latest.current++
    clear()
    setError('')
    if (file.current) file.current.value = ''
  }

  async function pick(e: ChangeEvent<HTMLInputElement>) {
    const f = e.target.files?.[0]
    setError('')
    clear()
    const n = ++latest.current
    if (!f) return
    setName(f.name)
    try {
      if (!TYPES.includes(f.type)) throw new Error("That file can't be used as a picture. Try a JPG or PNG.")
      if (f.size > MAX_FILE) throw new Error('The picture must be 2 MB or smaller.')
      const b = await square(f)
      if (n !== latest.current) return
      setBlob(b)
      setPreview((u) => { if (u) URL.revokeObjectURL(u); return URL.createObjectURL(b) })
    } catch (err) {
      if (n === latest.current) setError((err as Error).message)
    }
  }

  async function run(fn: () => Promise<unknown>) {
    setError('')
    setBusy(true)
    try {
      await fn()
      refreshAvatars()
      onChange()
      dialog.current?.close()
      return true
    } catch (err) {
      setError((err as Error).message)
      return false
    } finally {
      setBusy(false)
    }
  }

  const save = () => blob && run(() => api('/me/avatar', { method: 'PUT', body: blob, headers: { 'Content-Type': blob.type } }))
  const remove = async () => {
    if (await run(() => api('/me/avatar', { method: 'DELETE' }))) removed.current = true
    setConfirmRemove(false)
  }
  // removed: the Remove link is gone, so once the confirm has closed focus goes to the pencil
  useEffect(() => {
    if (!confirmRemove && removed.current) {
      removed.current = false
      pencil.current?.focus()
    }
  }, [confirmRemove])

  return (
    <>
      <span className="profile-avatar profile-avatar-lg">
        <Avatar userName={userName} />
        <button ref={pencil} type="button" className="edit-pencil" aria-label="Change profile picture" aria-haspopup="dialog" onClick={() => dialog.current?.showModal()}><Pencil /></button>
      </span>
      {/* the native dialog traps focus, closes on Escape and returns focus to the pencil */}
      <dialog ref={dialog} aria-labelledby={title} onClose={reset} onCancel={(e) => { if (busy) e.preventDefault() }}>
        <h3 id={title}>Profile picture</h3>
        {preview && <img className="avatar-preview" src={preview} alt="Preview of the cropped picture" />}
        <div className="avatar-file">
          <input ref={file} id={inputId} type="file" accept={TYPES.join(',')} disabled={busy} onChange={pick} />
          <label htmlFor={inputId}>Choose picture</label>
          {name && <span className="avatar-file-name muted">{name}</span>}
        </div>
        <p className="muted hint">Square pictures work best, ideally 256 x 256 px or larger. Keep your subject centered.</p>
        <p className="muted hint">Pictures are public, including to people you&apos;ve blocked.</p>
        {hasAvatar && <p><button type="button" className="link-button" disabled={busy} onClick={() => setConfirmRemove(true)}>Remove picture</button></p>}
        {error && <p className="error" role="alert">{error}</p>}
        <div className="row row-end">
          <button type="button" disabled={busy} onClick={() => dialog.current?.close()}>Cancel</button>
          <button type="button" className="on" disabled={busy || !blob} onClick={save}>{busy ? 'Working' : 'Save'}</button>
        </div>
      </dialog>
      <ConfirmDialog open={confirmRemove} title="Remove your profile picture?" confirmLabel="Remove" busy={busy} onConfirm={remove} onCancel={() => setConfirmRemove(false)} />
    </>
  )
}
