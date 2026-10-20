import { useEffect, useState } from 'react';
import { api, money } from '../api';

export default function Approvals() {
  const [reports, setReports] = useState([]);
  const [comments, setComments] = useState({});
  const [error, setError] = useState('');

  const load = () => api.pending().then(setReports).catch((e) => setError(e.message));
  useEffect(() => { load(); }, []);

  const act = async (kind, id) => {
    setError('');
    try { await api[kind](id, comments[id] || null); await load(); }
    catch (e) { setError(e.message); }
  };

  return (
    <section className="card wide">
      <h2>Reports waiting for your decision</h2>
      {error && <p className="error">{error}</p>}
      {reports.length === 0 && <p className="muted">Nothing to review.</p>}
      {reports.map((r) => (
        <div key={r.id} className="item">
          <div className="between">
            <strong>{r.title} - {r.employeeName}</strong>
            <span className={`badge ${r.status}`}>{r.status}</span>
          </div>
          <div className="muted">Total {money(r.total, r.currency)}</div>
          {r.items.map((i) => (
            <div key={i.id} className="between small"><span>{i.date} {i.merchant} ({i.category})</span><span>{money(i.amount, i.currency)}</span></div>
          ))}
          {r.violations.length > 0 && (
            <div className="warn"><strong>Policy findings</strong><ul>{r.violations.map((v, k) => <li key={k}>{v}</li>)}</ul></div>
          )}
          <input placeholder="Comment (required when rejecting)" value={comments[r.id] || ''}
                 onChange={(e) => setComments({ ...comments, [r.id]: e.target.value })} />
          <div className="row">
            <button className="primary" onClick={() => act('approve', r.id)}>Approve</button>
            <button className="danger" onClick={() => act('reject', r.id)}>Reject</button>
          </div>
        </div>
      ))}
    </section>
  );
}
