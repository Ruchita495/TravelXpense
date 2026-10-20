import { useEffect, useState } from 'react';
import { api, inDays, money } from '../api';

const emptyItem = () => ({ date: inDays(0).slice(0, 10), merchant: '', description: '', amount: '', category: '', receiptUrl: '' });

export default function Expenses() {
  const [reports, setReports] = useState([]);
  const [trips, setTrips] = useState([]);
  const [selected, setSelected] = useState(null);
  const [create, setCreate] = useState({ tripId: '', title: '' });
  const [item, setItem] = useState(emptyItem());
  const [error, setError] = useState('');

  const load = async () => {
    try {
      const [r, t] = await Promise.all([api.reports(), api.trips()]);
      setReports(r); setTrips(t);
      setSelected((cur) => (cur ? r.find((x) => x.id === cur.id) ?? null : null));
    } catch (e) { setError(e.message); }
  };
  useEffect(() => { load(); }, []);

  // After submit, a background worker processes the report. Poll until it finishes.
  useEffect(() => {
    if (selected?.status !== 'Submitted') return;
    const timer = setInterval(load, 1500);
    return () => clearInterval(timer);
  }, [selected?.status]);

  const run = async (fn) => { setError(''); try { await fn(); } catch (e) { setError(e.message); } };

  const createReport = (e) => { e.preventDefault(); run(async () => {
    const r = await api.createReport(create.tripId, create.title);
    setCreate({ ...create, title: '' }); await load(); setSelected(r);
  }); };

  const addItem = (e) => { e.preventDefault(); run(async () => {
    const body = { ...item, amount: Number(item.amount), category: item.category || null, receiptUrl: item.receiptUrl || null };
    const r = await api.addItem(selected.id, body);
    setItem(emptyItem()); setSelected(r); await load();
  }); };

  const submit = () => run(async () => { setSelected(await api.submit(selected.id)); await load(); });

  return (
    <div className="grid">
      <section className="card">
        <h2>New expense report</h2>
        <form onSubmit={createReport}>
          <select value={create.tripId} onChange={(e) => setCreate({ ...create, tripId: e.target.value })} required>
            <option value="">Select a trip...</option>
            {trips.map((t) => <option key={t.id} value={t.id}>{t.destination} ({t.startDate})</option>)}
          </select>
          <input placeholder="Report title" value={create.title} onChange={(e) => setCreate({ ...create, title: e.target.value })} required />
          <button className="primary">Create report</button>
        </form>
        {error && <p className="error">{error}</p>}
        <h2>My reports</h2>
        {reports.length === 0 && <p className="muted">No reports yet.</p>}
        {reports.map((r) => (
          <div key={r.id} className={`item clickable ${selected?.id === r.id ? 'sel' : ''}`} onClick={() => setSelected(r)}>
            <div className="between"><strong>{r.title}</strong><span className={`badge ${r.status}`}>{r.status}</span></div>
            <div className="muted">{money(r.total, r.currency)} - {r.items.length} items</div>
          </div>
        ))}
      </section>

      {selected && (
        <section className="card">
          <div className="between"><h2>{selected.title}</h2><span className={`badge ${selected.status}`}>{selected.status}</span></div>
          {selected.status === 'Submitted' && <p className="muted">Processing in the background (categorizing + policy check)...</p>}
          {selected.reviewerComment && <p><em>Reviewer: {selected.reviewerComment}</em></p>}

          {selected.items.map((i) => (
            <div key={i.id} className="item between">
              <span>{i.date} <strong>{i.merchant}</strong> <span className="badge">{i.category}{i.autoCategorized ? ' (auto)' : ''}</span></span>
              <strong>{money(i.amount, i.currency)}</strong>
            </div>
          ))}
          <p className="between"><span>Total</span><strong>{money(selected.total, selected.currency)}</strong></p>

          {selected.violations.length > 0 && (
            <div className="warn"><strong>Policy findings</strong>
              <ul>{selected.violations.map((v, idx) => <li key={idx}>{v}</li>)}</ul>
            </div>
          )}

          {selected.status === 'Draft' && (
            <>
              <h3>Add expense</h3>
              <form onSubmit={addItem}>
                <div className="row">
                  <input type="date" value={item.date} onChange={(e) => setItem({ ...item, date: e.target.value })} required />
                  <input placeholder="Merchant" value={item.merchant} onChange={(e) => setItem({ ...item, merchant: e.target.value })} required />
                </div>
                <div className="row">
                  <input type="number" step="0.01" min="0.01" placeholder="Amount (CAD)" value={item.amount} onChange={(e) => setItem({ ...item, amount: e.target.value })} required />
                  <select value={item.category} onChange={(e) => setItem({ ...item, category: e.target.value })}>
                    <option value="">Auto-categorize</option>
                    {['Airfare', 'Lodging', 'Meals', 'GroundTransport', 'Other'].map((c) => <option key={c}>{c}</option>)}
                  </select>
                </div>
                <input placeholder="Description (optional)" value={item.description} onChange={(e) => setItem({ ...item, description: e.target.value })} />
                <input placeholder="Receipt URL (optional, https://...)" value={item.receiptUrl} onChange={(e) => setItem({ ...item, receiptUrl: e.target.value })} />
                <button>Add item</button>
              </form>
              <button className="primary" disabled={selected.items.length === 0} onClick={submit}>Submit for approval</button>
            </>
          )}
        </section>
      )}
    </div>
  );
}
