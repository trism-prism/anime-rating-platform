// scores are out of 10
export default function ScoreBar({ label, value }: { label: string; value: number }) {
  return (
    <div className="score-row">
      <span className="score-label">{label}</span>
      <div className="bar">
        <div className={label === 'Overall' ? 'bar-fill lead' : 'bar-fill'} style={{ width: `${value * 10}%` }} />
      </div>
      <span className="score-value">{value}</span>
    </div>
  )
}
