import { useEffect, useState } from 'react';
import { api, inDays, money } from '../api';

export default function Trips() {
  const [trips, setTrips] = useState([]);
  const [error, setError] = useState('');
  const [form, setForm] = useState({ destination: '', startDate: inDays(14), endDate: inDays(16), purpose: '' });
  const [active, setActive] = useState(null);
  const [search, setSearch] = useState({ origin: 'YQG', destination: 'YYZ', date: inDays(14) });
  const [result, setResult] = useState(null);

  const load = () => api.trips().then(setTrips).catch((e) => setError(e.message));
  useEffect(() => { load(); }, []);

  const set = (k) => (e) => setForm({ ...form, [k]: e.target.value });
  const setS = (k) => (e) => setSearch({ ...search, [k]: k === 'date' ? e.target.value : e.target.value.toUpperCase() });

  const createTrip = async (e) => {
    e.preventDefault(); setError('');
    try { await api.createTrip(form); setForm({ ...form, destination: '', purpose: '' }); await load(); }
    catch (err) { setError(err.message); }
  };

  const doSearch = async (e) => {
    e.preventDefault(); setError('');
    try { setResult(await api.searchFlights(search)); } catch (err) { setError(err.message); }
  };

  const book = async (offer) => {
    setError('');
    try {
      await api.bookFlight(active.id, { offerId: offer.id, origin: offer.origin, destination: offer.destination, date: search.date });
      await load();
      setActive(null); setResult(null);
    } catch (err) { setError(err.message); }
  };

  return (
    <div className="grid">
      <section className="card">
        <h2>New trip request</h2>
        <form onSubmit={createTrip}>
          <input placeholder="Destination (e.g. Toronto)" value={form.destination} onChange={set('destination')} required />
          <div className="row">
            <input type="date" value={form.startDate} onChange={set('startDate')} required />
            <input type="date" value={form.endDate} onChange={set('endDate')} required />
          </div>
          <input placeholder="Purpose (e.g. Client workshop)" value={form.purpose} onChange={set('purpose')} required />
          <button className="primary">Create trip</button>
        </form>
        {error && <p className="error">{error}</p>}
      </section>

      <section className="card">
        <h2>My trips</h2>
        {trips.length === 0 && <p className="muted">No trips yet.</p>}
        {trips.map((t) => (
          <div key={t.id} className="item">
            <div className="between">
              <strong>{t.destination}</strong>
              <span className="badge">{t.status}</span>
            </div>
            <div className="muted">{t.startDate} to {t.endDate} - {t.purpose}</div>
            {t.bookings.map((b) => (
              <div key={b.id} className="booking">
                {b.provider} {b.cabin} - {b.description} - <strong>{money(b.price, b.currency)}</strong> (ref {b.reference})
              </div>
            ))}
            <button onClick={() => { setActive(t); setResult(null); }}>Search &amp; book a flight</button>
          </div>
        ))}
      </section>

      {active && (
        <section className="card wide">
          <h2>Flights for {active.destination}</h2>
          <form onSubmit={doSearch} className="row">
            <input value={search.origin} onChange={setS('origin')} maxLength={3} placeholder="From (YQG)" />
            <input value={search.destination} onChange={setS('destination')} maxLength={3} placeholder="To (YYZ)" />
            <input type="date" value={search.date} onChange={setS('date')} />
            <button className="primary">Search</button>
            <button type="button" onClick={() => setActive(null)}>Close</button>
          </form>
          {result && (
            <>
              <p className="muted">{result.offers.length} offers - {result.cacheHit ? 'served from cache (fast)' : 'fresh from provider'}</p>
              {result.offers.map((o) => (
                <div key={o.id} className="item between">
                  <span>
                    <strong>{o.airline}</strong> {o.origin} to {o.destination}{' '}
                    {new Date(o.departure).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                    {' - '}{new Date(o.arrival).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}{' '}
                    <span className="badge">{o.cabin}</span>
                  </span>
                  <span>
                    <strong>{money(o.price, o.currency)}</strong>{' '}
                    <button className="primary" onClick={() => book(o)}>Book</button>
                  </span>
                </div>
              ))}
            </>
          )}
        </section>
      )}
    </div>
  );
}
