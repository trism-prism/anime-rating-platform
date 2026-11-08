import { useId, useRef, useState, type ChangeEvent } from 'react'
import { api } from '../api'
import { refreshAvatars } from '../avatarVersion'
import Avatar from './Avatar'
import { Pencil } from './InlineEdit'

const TYPES = ['image/png', 'image/jpeg', 'image/webp']
const MAX_FILE = 2 * 1024 * 1024
const SIZE = 256

// center-crop to a square and scale to 256x256, so what is stored is small whatever was chosen
async function square(file: File): Promise<Blob> {
  const img = await createImageBitmap(file).catch(() => { throw new Error('Could not read that image.') })
  try {
    const side = Math.min(img.width, img.height)
    const canvas = document.createElement('canvas')
    canvas.width = canvas.height = SIZE
    canvas.getContext('2d')!.drawImage(img, (img.width - side) / 2, (img.height - side) / 2, side, side, 0, 0, SIZE, SIZE)
    // webp, or png where the browser can't write webp; the API looks at the bytes either way
    return new Promise((res, rej) => canvas.toBlob((b) => (b ? res(b) : rej(new Error('Could not read that image.'))), 'image/webp', 0.9))
  } finally {
    img.close()
  }
}

// your own picture, large; the pencil (on hover or focus) opens a dialog to choose, preview, save or remove it
export default function AvatarEditor({ userName, hasAvatar, onChange }: { userName: string; hasAvatar: boolean; onChange: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null)
  const file = useRef<HTMLInputElement>(null)
  const title = useId()
  const inputId = useId()
  const [name, setName] = useState('')
  const [blob, setBlob] = useState<Blob | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [preview, setPreview] = useState('')

  function clear() {
    setBlob(null)
    setName('')
    setPreview((u) => { if (u) URL.revokeObjectURL(u); return '' })
  }

  function reset() {
    clear()
    setError('')
    if (file.current) file.current.value = ''
  }

  async function pick(e: ChangeEvent<HTMLInputElement>) {
    const f = e.target.files?.[0]
    setError('')
    clear()
    if (!f) return
    setName(f.name)
    try {
      if (!TYPES.includes(f.type)) throw new Error('Choose a png, jpeg or webp image.')
      if (f.size > MAX_FILE) throw new Error('The picture must be 2 MB or smaller.')
      const b = await square(f)
      setBlob(b)
      setPreview((u) => { if (u) URL.revokeObjectURL(u); return URL.createObjectURL(b) })
    } catch (err) {
      setError((err as Error).message)
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
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const save = () => blob && run(() => api('/me/avatar', { method: 'PUT', body: blob, headers: { 'Content-Type': blob.type } }))
  const remove = () => run(() => api('/me/avatar', { method: 'DELETE' }))

  return (
    <>
      <span className="profile-avatar profile-avatar-lg">
        <Avatar userName={userName} />
        <button type="button" className="edit-pencil" aria-label="Change profile picture" aria-haspopup="dialog" onClick={() => dialog.current?.showModal()}><Pencil /></button>
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
        <p className="muted hint">A png, jpeg or webp image up to 2 MB. Use a square image, ideally 256 x 256 px or larger. It is cropped to a 256 x 256 circle view, so keep the subject centered.</p>
        <p className="muted hint">Pictures are public, including to people you have blocked.</p>
        {error && <p className="error" role="alert">{error}</p>}
        <div className="row row-end">
          {hasAvatar && <button type="button" disabled={busy} onClick={remove}>Remove</button>}
          <button type="button" disabled={busy} onClick={() => dialog.current?.close()}>Cancel</button>
          <button type="button" className="on" disabled={busy || !blob} onClick={save}>{busy ? 'Working' : 'Save'}</button>
        </div>
      </dialog>
    </>
  )
}
