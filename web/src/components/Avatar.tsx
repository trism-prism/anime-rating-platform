// Round avatar: the picture when there is one, else the first letter of the name.
// (Profile pictures come later; imageUrl is here so callers do not change then.)
export default function Avatar({ userName, imageUrl, small }: { userName: string; imageUrl?: string; small?: boolean }) {
  return (
    <span className={small ? 'avatar small' : 'avatar'} aria-hidden="true">
      {imageUrl ? <img src={imageUrl} alt="" /> : userName.charAt(0).toUpperCase()}
    </span>
  )
}
