import { useEffect, useRef, useState } from 'react'

// A cover in a fixed 2:3 box with a tinted background, so nothing jumps while the image loads or when it is missing.
// The first letter of the name shows underneath until the picture fades in (and stays when there is none).
// Size comes from the parent's CSS (width), the box brings the height. `eager` is for covers above the fold.
export default function Cover({ src, name, eager = false, alt = '', className = '' }: {
  src: string | null | undefined
  name: string
  eager?: boolean
  alt?: string
  className?: string
}) {
  const [loaded, setLoaded] = useState<string | null>(null) // the src that finished loading
  const [failed, setFailed] = useState<string | null>(null) // the src that did not
  const img = useRef<HTMLImageElement>(null)

  // a cached image can finish before React attaches onLoad
  useEffect(() => {
    const i = img.current
    if (i?.complete && src) {
      if (i.naturalWidth > 0) setLoaded(src)
      else setFailed(src)
    }
  }, [src])

  const showImage = !!src && failed !== src
  const letter = ([...name.trim()][0] ?? '?').toUpperCase()

  return (
    <span className={`cover-box ${className}`.trim()}>
      <span className="cover-letter" aria-hidden="true">{letter}</span>
      {showImage && (
        <img
          ref={img}
          src={src}
          alt={alt}
          className={loaded === src ? 'loaded' : ''}
          loading={eager ? 'eager' : 'lazy'}
          fetchPriority={eager ? 'high' : 'auto'}
          decoding="async"
          onLoad={() => setLoaded(src)}
          onError={() => setFailed(src)}
        />
      )}
    </span>
  )
}
