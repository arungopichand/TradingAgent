'use client';

import { FormEvent, useEffect, useState } from 'react';

const configuredApiBase = process.env.NEXT_PUBLIC_API_BASE_URL?.trim().replace(/\/$/, '');

type DashboardTab = 'analysis' | 'watchlists' | 'intraday' | 'penny' | 'portfolio' | 'trades' | 'alerts';
type LiveStreamStatus = 'idle' | 'connecting' | 'live' | 'reconnecting';
type WatchlistListType = 'analysis' | 'background' | 'day_trading' | 'penny_stock';

interface AnalysisResult {
  symbol: string;
  price: number;
  rsi: number;
  trend: string;
  action: string;
  confidence: number;
  reason: string;
}

interface PortfolioPosition {
  id: number;
  symbol: string;
  quantity: number;
  entryPrice: number;
  entryDate: string;
  stopLoss?: number | null;
  takeProfit?: number | null;
  notes: string;
  createdAt: string;
  updatedAt: string;
}

interface Trade {
  id: number;
  symbol: string;
  side: string;
  quantity: number;
  entryPrice: number;
  entryDate: string;
  exitPrice?: number | null;
  exitDate?: string | null;
  pnl?: number | null;
  pnlPercent?: number | null;
  exitReason: string;
  notes: string;
  createdAt: string;
}

interface AlertItem {
  id: number;
  symbol: string;
  alertType: string;
  threshold: number;
  condition: string;
  isActive: boolean;
  isTriggered: boolean;
  triggeredAt?: string | null;
  expiresAt?: string | null;
  createdAt: string;
}

interface WatchlistEntry {
  id: number;
  listType: WatchlistListType;
  symbol: string;
  sortOrder: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

interface DayTradingIdea {
  symbol: string;
  action: string;
  direction: string;
  setup: string;
  currentPrice: number;
  entryPrice: number;
  stopLoss: number;
  targetPrice1: number;
  targetPrice2: number;
  riskRewardRatio: number;
  rsi: number;
  momentumPercent: number;
  dayChangePercent: number;
  volume: number;
  confidence: number;
  whyItWasPicked: string;
  whatToDo: string;
  whenToSell: string;
  beginnerTip: string;
  lastUpdatedUtc: string;
}

interface DayTradingBoard {
  generatedAtUtc: string;
  timeframe: string;
  marketStatus: string;
  beginnerNote: string;
  picks: DayTradingIdea[];
}

const normalizeBoard = (payload: Partial<DayTradingBoard> | null | undefined): DayTradingBoard | null => {
  if (!payload) {
    return null;
  }

  return {
    generatedAtUtc: payload.generatedAtUtc ?? '',
    timeframe: payload.timeframe ?? '',
    marketStatus: payload.marketStatus ?? '',
    beginnerNote: payload.beginnerNote ?? '',
    picks: Array.isArray(payload.picks) ? payload.picks : [],
  };
};

const watchlistGroups: { key: WatchlistListType; label: string; description: string }[] = [
  {
    key: 'analysis',
    label: 'Analysis Feed',
    description: 'Symbols used by the AI analysis tab.',
  },
  {
    key: 'background',
    label: 'Background Sync',
    description: 'Symbols the backend keeps refreshing into local history.',
  },
  {
    key: 'day_trading',
    label: 'Day Trading Scanner',
    description: 'Symbols scanned for live intraday ideas.',
  },
  {
    key: 'penny_stock',
    label: 'Penny Stock Scanner',
    description: 'Symbols scanned for sub-$10 momentum ideas.',
  },
];

export default function Home() {
  const apiBase = configuredApiBase ?? '';

  const [activeTab, setActiveTab] = useState<DashboardTab>('analysis');
  const [analysis, setAnalysis] = useState<AnalysisResult[]>([]);
  const [dayTradingBoard, setDayTradingBoard] = useState<DayTradingBoard | null>(null);
  const [pennyStockBoard, setPennyStockBoard] = useState<DayTradingBoard | null>(null);
  const [portfolio, setPortfolio] = useState<PortfolioPosition[]>([]);
  const [trades, setTrades] = useState<Trade[]>([]);
  const [alerts, setAlerts] = useState<AlertItem[]>([]);
  const [watchlists, setWatchlists] = useState<WatchlistEntry[]>([]);
  const [loading, setLoading] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [analysisUpdatedAt, setAnalysisUpdatedAt] = useState<string | null>(null);
  const [watchlistsUpdatedAt, setWatchlistsUpdatedAt] = useState<string | null>(null);
  const [dayTradingUpdatedAt, setDayTradingUpdatedAt] = useState<string | null>(null);
  const [pennyStocksUpdatedAt, setPennyStocksUpdatedAt] = useState<string | null>(null);
  const [intradayStreamStatus, setIntradayStreamStatus] = useState<LiveStreamStatus>('idle');
  const [pennyStreamStatus, setPennyStreamStatus] = useState<LiveStreamStatus>('idle');
  const [watchlistForm, setWatchlistForm] = useState<{ listType: WatchlistListType; symbol: string }>({
    listType: 'day_trading',
    symbol: '',
  });
  const [portfolioForm, setPortfolioForm] = useState({
    symbol: '',
    quantity: 0,
    entryPrice: 0,
    entryDate: new Date().toISOString().slice(0, 10),
    stopLoss: '',
    takeProfit: '',
    notes: '',
  });
  const [tradeForm, setTradeForm] = useState({
    symbol: '',
    side: 'buy',
    quantity: 0,
    entryPrice: 0,
    entryDate: new Date().toISOString().slice(0, 10),
    exitPrice: '',
    exitDate: '',
    pnl: '',
    pnlPercent: '',
    exitReason: '',
    notes: '',
  });
  const [alertForm, setAlertForm] = useState({
    symbol: '',
    alertType: 'price_above',
    threshold: 0,
    condition: '',
    expiresAt: '',
  });
  const [editingAlertId, setEditingAlertId] = useState<number | null>(null);

  useEffect(() => {
    if (activeTab === 'analysis') {
      fetchAnalysis();
    } else if (activeTab === 'watchlists') {
      fetchWatchlists();
    } else if (activeTab === 'intraday') {
      fetchDayTrading();
    } else if (activeTab === 'penny') {
      fetchPennyStocks();
    } else if (activeTab === 'portfolio') {
      fetchPortfolio();
    } else if (activeTab === 'trades') {
      fetchTrades();
    } else if (activeTab === 'alerts') {
      fetchAlerts();
    }
  }, [activeTab]);

  useEffect(() => {
    const interval = setInterval(() => {
      if (activeTab === 'analysis') {
        fetchAnalysis();
      }
    }, 300000);

    return () => clearInterval(interval);
  }, [activeTab]);

  useEffect(() => {
    if (activeTab !== 'intraday') {
      setIntradayStreamStatus('idle');
      return;
    }

    setIntradayStreamStatus('connecting');
    const source = new EventSource(`${apiBase}/stream/trade/intraday`);

    source.onmessage = (event) => {
      try {
        const board = normalizeBoard(JSON.parse(event.data) as Partial<DayTradingBoard>);
        setDayTradingBoard(board);
        setDayTradingUpdatedAt(new Date().toLocaleTimeString());
        setError(null);
        setLoading(false);
        setIntradayStreamStatus('live');
      } catch {
        setIntradayStreamStatus('reconnecting');
      }
    };

    source.onerror = () => {
      setIntradayStreamStatus((current) => (current === 'live' ? 'reconnecting' : 'connecting'));
    };

    return () => {
      source.close();
      setIntradayStreamStatus('idle');
    };
  }, [activeTab, apiBase]);

  useEffect(() => {
    if (activeTab !== 'penny') {
      setPennyStreamStatus('idle');
      return;
    }

    setPennyStreamStatus('connecting');
    const source = new EventSource(`${apiBase}/stream/trade/penny-stocks`);

    source.onmessage = (event) => {
      try {
        const board = normalizeBoard(JSON.parse(event.data) as Partial<DayTradingBoard>);
        setPennyStockBoard(board);
        setPennyStocksUpdatedAt(new Date().toLocaleTimeString());
        setError(null);
        setLoading(false);
        setPennyStreamStatus('live');
      } catch {
        setPennyStreamStatus('reconnecting');
      }
    };

    source.onerror = () => {
      setPennyStreamStatus((current) => (current === 'live' ? 'reconnecting' : 'connecting'));
    };

    return () => {
      source.close();
      setPennyStreamStatus('idle');
    };
  }, [activeTab, apiBase]);

  useEffect(() => {
    if (activeTab !== 'alerts') {
      return;
    }

    const interval = setInterval(fetchAlerts, 30000);
    return () => clearInterval(interval);
  }, [activeTab]);

  const handleApiError = async (response: Response) => {
    const text = await response.text();
    try {
      const json = JSON.parse(text);
      return json.error ?? response.statusText;
    } catch {
      return response.statusText || 'Unknown error';
    }
  };

  const fetchAnalysis = async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await fetch(`${apiBase}/trade/analyze`);
      if (!response.ok) {
        throw new Error(await handleApiError(response));
      }
      const data = await response.json();
      setAnalysis(Array.isArray(data) ? data : [data]);
      setAnalysisUpdatedAt(new Date().toLocaleTimeString());
    } catch (err) {
      setAnalysis([]);
      setError(err instanceof Error ? err.message : 'Failed to load analysis');
    } finally {
      setLoading(false);
    }
  };

  const fetchDayTrading = async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await fetch(`${apiBase}/trade/intraday`);
      if (!response.ok) {
        throw new Error(await handleApiError(response));
      }

      setDayTradingBoard(await response.json());
      setDayTradingUpdatedAt(new Date().toLocaleTimeString());
    } catch (err) {
      setDayTradingBoard(null);
      setError(err instanceof Error ? err.message : 'Failed to load day trading ideas');
    } finally {
      setLoading(false);
    }
  };

  const fetchPennyStocks = async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await fetch(`${apiBase}/trade/penny-stocks`);
      if (!response.ok) {
        throw new Error(await handleApiError(response));
      }

      setPennyStockBoard(await response.json());
      setPennyStocksUpdatedAt(new Date().toLocaleTimeString());
    } catch (err) {
      setPennyStockBoard(null);
      setError(err instanceof Error ? err.message : 'Failed to load penny stock ideas');
    } finally {
      setLoading(false);
    }
  };

  const fetchPortfolio = async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await fetch(`${apiBase}/portfolio`);
      if (!response.ok) {
        throw new Error(await handleApiError(response));
      }
      setPortfolio(await response.json());
    } catch (err) {
      setPortfolio([]);
      setError(err instanceof Error ? err.message : 'Failed to load portfolio');
    } finally {
      setLoading(false);
    }
  };

  const fetchTrades = async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await fetch(`${apiBase}/trades`);
      if (!response.ok) {
        throw new Error(await handleApiError(response));
      }
      setTrades(await response.json());
    } catch (err) {
      setTrades([]);
      setError(err instanceof Error ? err.message : 'Failed to load trades');
    } finally {
      setLoading(false);
    }
  };

  const fetchAlerts = async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await fetch(`${apiBase}/alerts`);
      if (!response.ok) {
        throw new Error(await handleApiError(response));
      }
      setAlerts(await response.json());
    } catch (err) {
      setAlerts([]);
      setError(err instanceof Error ? err.message : 'Failed to load alerts');
    } finally {
      setLoading(false);
    }
  };

  const fetchWatchlists = async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await fetch(`${apiBase}/watchlists`);
      if (!response.ok) {
        throw new Error(await handleApiError(response));
      }

      setWatchlists(await response.json());
      setWatchlistsUpdatedAt(new Date().toLocaleTimeString());
    } catch (err) {
      setWatchlists([]);
      setError(err instanceof Error ? err.message : 'Failed to load watchlists');
    } finally {
      setLoading(false);
    }
  };

  const submitWatchlist = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError(null);
    setMessage(null);

    try {
      const response = await fetch(`${apiBase}/watchlists`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          listType: watchlistForm.listType,
          symbol: watchlistForm.symbol,
        }),
      });

      if (!response.ok) {
        throw new Error(await handleApiError(response));
      }

      await fetchWatchlists();
      setMessage('Watchlist updated successfully');
      setWatchlistForm((current) => ({ ...current, symbol: '' }));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to add watchlist symbol');
    }
  };

  const deleteWatchlistEntry = async (id: number) => {
    setError(null);
    setMessage(null);

    try {
      const response = await fetch(`${apiBase}/watchlists/${id}`, { method: 'DELETE' });
      if (!response.ok) {
        throw new Error(await handleApiError(response));
      }

      await fetchWatchlists();
      setMessage('Watchlist symbol removed');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to remove watchlist symbol');
    }
  };

  const submitPortfolio = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError(null);
    setMessage(null);

    try {
      const response = await fetch(`${apiBase}/portfolio`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          symbol: portfolioForm.symbol,
          quantity: Number(portfolioForm.quantity),
          entryPrice: Number(portfolioForm.entryPrice),
          entryDate: portfolioForm.entryDate,
          stopLoss: portfolioForm.stopLoss ? Number(portfolioForm.stopLoss) : null,
          takeProfit: portfolioForm.takeProfit ? Number(portfolioForm.takeProfit) : null,
          notes: portfolioForm.notes,
        }),
      });

      if (!response.ok) {
        throw new Error(await handleApiError(response));
      }

      await fetchPortfolio();
      setMessage('Portfolio position added successfully');
      setPortfolioForm({
        symbol: '',
        quantity: 0,
        entryPrice: 0,
        entryDate: new Date().toISOString().slice(0, 10),
        stopLoss: '',
        takeProfit: '',
        notes: '',
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create portfolio position');
    }
  };

  const submitTrade = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError(null);
    setMessage(null);

    try {
      const response = await fetch(`${apiBase}/trades`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          symbol: tradeForm.symbol,
          side: tradeForm.side,
          quantity: Number(tradeForm.quantity),
          entryPrice: Number(tradeForm.entryPrice),
          entryDate: tradeForm.entryDate,
          exitPrice: tradeForm.exitPrice ? Number(tradeForm.exitPrice) : null,
          exitDate: tradeForm.exitDate || null,
          pnl: tradeForm.pnl ? Number(tradeForm.pnl) : null,
          pnlPercent: tradeForm.pnlPercent ? Number(tradeForm.pnlPercent) : null,
          exitReason: tradeForm.exitReason,
          notes: tradeForm.notes,
        }),
      });

      if (!response.ok) {
        throw new Error(await handleApiError(response));
      }

      await fetchTrades();
      setMessage('Trade added successfully');
      setTradeForm({
        symbol: '',
        side: 'buy',
        quantity: 0,
        entryPrice: 0,
        entryDate: new Date().toISOString().slice(0, 10),
        exitPrice: '',
        exitDate: '',
        pnl: '',
        pnlPercent: '',
        exitReason: '',
        notes: '',
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create trade');
    }
  };

  const deletePortfolioPosition = async (id: number) => {
    setError(null);
    setMessage(null);
    try {
      const response = await fetch(`${apiBase}/portfolio/${id}`, { method: 'DELETE' });
      if (!response.ok) {
        throw new Error(await handleApiError(response));
      }
      await fetchPortfolio();
      setMessage('Portfolio position removed');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to remove position');
    }
  };

  const deleteTrade = async (id: number) => {
    setError(null);
    setMessage(null);
    try {
      const response = await fetch(`${apiBase}/trades/${id}`, { method: 'DELETE' });
      if (!response.ok) {
        throw new Error(await handleApiError(response));
      }
      await fetchTrades();
      setMessage('Trade removed successfully');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to remove trade');
    }
  };

  const submitAlert = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError(null);
    setMessage(null);

    try {
      const url = editingAlertId ? `${apiBase}/alerts/${editingAlertId}` : `${apiBase}/alerts`;
      const method = editingAlertId ? 'PUT' : 'POST';

      const response = await fetch(url, {
        method,
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          symbol: alertForm.symbol,
          alertType: alertForm.alertType,
          threshold: Number(alertForm.threshold),
          condition: alertForm.condition,
          isActive: true,
          expiresAt: alertForm.expiresAt || null,
        }),
      });

      if (!response.ok) {
        throw new Error(await handleApiError(response));
      }

      await fetchAlerts();
      setMessage(editingAlertId ? 'Alert updated successfully' : 'Alert created successfully');
      setAlertForm({
        symbol: '',
        alertType: 'price_above',
        threshold: 0,
        condition: '',
        expiresAt: '',
      });
      setEditingAlertId(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save alert');
    }
  };

  const startEditAlert = (alert: AlertItem) => {
    setEditingAlertId(alert.id);
    setAlertForm({
      symbol: alert.symbol,
      alertType: alert.alertType,
      threshold: alert.threshold,
      condition: alert.condition,
      expiresAt: alert.expiresAt ? alert.expiresAt.slice(0, 10) : '',
    });
  };

  const cancelEditAlert = () => {
    setEditingAlertId(null);
    setAlertForm({
      symbol: '',
      alertType: 'price_above',
      threshold: 0,
      condition: '',
      expiresAt: '',
    });
  };

  const deleteAlert = async (id: number) => {
    setError(null);
    setMessage(null);
    try {
      const response = await fetch(`${apiBase}/alerts/${id}`, { method: 'DELETE' });
      if (!response.ok) {
        throw new Error(await handleApiError(response));
      }
      await fetchAlerts();
      setMessage('Alert removed successfully');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to remove alert');
    }
  };

  const activeAlerts = alerts.filter((alert) => !alert.isTriggered);
  const triggeredAlerts = alerts.filter((alert) => alert.isTriggered);
  const activeUpdatedAt =
    activeTab === 'analysis'
      ? analysisUpdatedAt
      : activeTab === 'watchlists'
      ? watchlistsUpdatedAt
      : activeTab === 'intraday'
      ? dayTradingUpdatedAt
      : activeTab === 'penny'
      ? pennyStocksUpdatedAt
      : null;
  const activeLiveStatus =
    activeTab === 'intraday'
      ? intradayStreamStatus
      : activeTab === 'penny'
      ? pennyStreamStatus
      : 'idle';
  const formatCurrency = (value: number) => `$${value.toFixed(2)}`;
  const formatSignedPercent = (value: number) => `${value >= 0 ? '+' : ''}${value.toFixed(2)}%`;

  const renderTabs = () => {
    const tabs = [
      { key: 'analysis', label: 'Analysis' },
      { key: 'watchlists', label: 'Watchlists' },
      { key: 'intraday', label: 'Day Trading' },
      { key: 'penny', label: 'Penny Stocks' },
      { key: 'portfolio', label: 'Portfolio' },
      { key: 'trades', label: 'Trades' },
      { key: 'alerts', label: 'Alerts' },
    ] satisfies { key: DashboardTab; label: string }[];

    return (
      <div className="mb-6 flex flex-col sm:flex-row gap-3 sm:items-center sm:justify-between">
        <div className="flex flex-wrap gap-2">
          {tabs.map((tab) => (
            <button
              key={tab.key}
              onClick={() => setActiveTab(tab.key)}
              className={`rounded-full px-4 py-2 text-sm font-semibold transition ${
                activeTab === tab.key
                  ? 'bg-slate-100 text-slate-950'
                  : 'bg-slate-800 text-slate-300 hover:bg-slate-700'
              }`}
            >
              {tab.label}
            </button>
          ))}
        </div>
        <div className="flex flex-wrap items-center gap-3 text-sm text-slate-400">
          {activeUpdatedAt && <div>Last refreshed at {activeUpdatedAt}</div>}
          {(activeTab === 'intraday' || activeTab === 'penny') && (
            <div
              className={`rounded-full px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em] ${
                activeLiveStatus === 'live'
                  ? 'bg-emerald-500/15 text-emerald-300'
                  : activeLiveStatus === 'reconnecting'
                  ? 'bg-amber-500/15 text-amber-300'
                  : 'bg-slate-800 text-slate-300'
              }`}
            >
              {activeLiveStatus === 'live'
                ? 'Live Stream On'
                : activeLiveStatus === 'reconnecting'
                ? 'Reconnecting'
                : 'Connecting'}
            </div>
          )}
        </div>
      </div>
    );
  };

  return (
    <div className="min-h-screen bg-slate-950 text-slate-100">
      <div className="mx-auto max-w-7xl px-4 py-8">
        <header className="mb-8">
          <h1 className="text-4xl font-bold">Krish Agent</h1>
          <p className="mt-2 text-slate-400">AI-powered trading dashboard with portfolio and trade tracking.</p>
        </header>

        {renderTabs()}

        {(message || error) && (
          <div
            className={`mb-6 rounded-2xl border p-4 text-sm shadow-lg ${
              error ? 'border-rose-500 bg-rose-950/60' : 'border-emerald-500 bg-emerald-950/60'
            }`}
          >
            <p className={error ? 'text-rose-200' : 'text-emerald-200'}>{error ?? message}</p>
          </div>
        )}

        {activeTab === 'intraday' && (
          <section>
            <div className="rounded-[2rem] border border-slate-800 bg-gradient-to-br from-slate-900 via-slate-900 to-emerald-950/40 p-6 shadow-lg shadow-slate-950/40">
              <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
                <div className="max-w-3xl">
                  <p className="text-sm uppercase tracking-[0.25em] text-emerald-300/80">Day Trading</p>
                  <h2 className="mt-2 text-3xl font-semibold text-white">Five live intraday setups for beginners</h2>
                  <p className="mt-3 text-sm leading-6 text-slate-300">
                    This tab scans a liquid watchlist using live intraday market data and gives simple entry, stop loss,
                    and exit plans. Green cards are long ideas. Red cards are short ideas, so beginners can skip them if
                    they do not short stocks.
                  </p>
                </div>
                <button
                  onClick={fetchDayTrading}
                  className="rounded-full bg-emerald-400 px-5 py-2.5 text-sm font-semibold text-slate-950 transition hover:bg-emerald-300"
                >
                  Refresh Ideas
                </button>
              </div>

              {dayTradingBoard && (
                <div className="mt-6 grid gap-4 lg:grid-cols-3">
                  <div className="rounded-3xl border border-slate-800 bg-slate-950/70 p-4">
                    <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Timeframe</p>
                    <p className="mt-2 text-sm text-slate-200">{dayTradingBoard.timeframe}</p>
                  </div>
                  <div className="rounded-3xl border border-slate-800 bg-slate-950/70 p-4">
                    <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Market Note</p>
                    <p className="mt-2 text-sm text-slate-200">{dayTradingBoard.marketStatus}</p>
                  </div>
                  <div className="rounded-3xl border border-slate-800 bg-slate-950/70 p-4">
                    <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Beginner Note</p>
                    <p className="mt-2 text-sm text-slate-200">{dayTradingBoard.beginnerNote}</p>
                  </div>
                </div>
              )}
            </div>

            {loading && <div className="mt-6 rounded-3xl bg-slate-900 p-8 text-center text-slate-400">Loading day trading ideas...</div>}

            {!loading && error && <div className="mt-6 rounded-3xl bg-rose-950 p-6 text-slate-200">{error}</div>}

            {!loading && !error && (!dayTradingBoard || dayTradingBoard.picks.length === 0) && (
              <div className="mt-6 rounded-3xl bg-slate-900 p-8 text-center text-slate-400">
                No intraday setups are ready yet. This is common near the open or during quieter tape, so give the scanner another minute and refresh.
              </div>
            )}

            {!loading && !error && dayTradingBoard && dayTradingBoard.picks.length > 0 && (
              <div className="mt-6 grid gap-5 xl:grid-cols-2">
                {dayTradingBoard.picks.map((idea, index) => (
                  <article
                    key={idea.symbol}
                    className={`rounded-[2rem] border p-6 shadow-lg shadow-slate-950/40 ${
                      idea.action === 'buy'
                        ? 'border-emerald-500/20 bg-emerald-950/10'
                        : 'border-rose-500/20 bg-rose-950/10'
                    }`}
                  >
                    <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
                      <div>
                        <p className="text-xs uppercase tracking-[0.25em] text-slate-500">Pick {index + 1}</p>
                        <h3 className="mt-2 text-3xl font-semibold text-white">{idea.symbol}</h3>
                        <p className="mt-2 text-sm text-slate-300">{idea.setup}</p>
                      </div>
                      <div className="flex flex-wrap gap-2">
                        <span
                          className={`inline-flex rounded-full px-3 py-1 text-xs font-semibold ${
                            idea.action === 'buy'
                              ? 'bg-emerald-400/15 text-emerald-300'
                              : 'bg-rose-400/15 text-rose-300'
                          }`}
                        >
                          {idea.action === 'buy' ? 'BUY / LONG' : 'SELL / SHORT'}
                        </span>
                        <span className="inline-flex rounded-full bg-slate-950 px-3 py-1 text-xs font-semibold text-slate-300">
                          {idea.confidence}% confidence
                        </span>
                      </div>
                    </div>

                    <div className="mt-5 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                      <div className="rounded-3xl bg-slate-950/80 p-4">
                        <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Current</p>
                        <p className="mt-2 text-xl font-semibold text-white">{formatCurrency(idea.currentPrice)}</p>
                      </div>
                      <div className="rounded-3xl bg-slate-950/80 p-4">
                        <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Entry</p>
                        <p className="mt-2 text-xl font-semibold text-white">{formatCurrency(idea.entryPrice)}</p>
                      </div>
                      <div className="rounded-3xl bg-slate-950/80 p-4">
                        <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Stop Loss</p>
                        <p className="mt-2 text-xl font-semibold text-white">{formatCurrency(idea.stopLoss)}</p>
                      </div>
                      <div className="rounded-3xl bg-slate-950/80 p-4">
                        <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Reward / Risk</p>
                        <p className="mt-2 text-xl font-semibold text-white">{idea.riskRewardRatio.toFixed(2)}R</p>
                      </div>
                    </div>

                    <div className="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                      <div className="rounded-3xl border border-slate-800 bg-slate-950/60 p-4">
                        <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Target 1</p>
                        <p className="mt-2 text-lg font-semibold text-white">{formatCurrency(idea.targetPrice1)}</p>
                      </div>
                      <div className="rounded-3xl border border-slate-800 bg-slate-950/60 p-4">
                        <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Target 2</p>
                        <p className="mt-2 text-lg font-semibold text-white">{formatCurrency(idea.targetPrice2)}</p>
                      </div>
                      <div className="rounded-3xl border border-slate-800 bg-slate-950/60 p-4">
                        <p className="text-xs uppercase tracking-[0.2em] text-slate-500">RSI / Momentum</p>
                        <p className="mt-2 text-sm text-slate-200">RSI {idea.rsi.toFixed(2)} / {formatSignedPercent(idea.momentumPercent)}</p>
                      </div>
                      <div className="rounded-3xl border border-slate-800 bg-slate-950/60 p-4">
                        <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Day Change / Volume</p>
                        <p className="mt-2 text-sm text-slate-200">{formatSignedPercent(idea.dayChangePercent)} / {idea.volume.toLocaleString()} shares</p>
                      </div>
                    </div>

                    <div className="mt-5 grid gap-4 lg:grid-cols-[1.15fr_1fr]">
                      <div className="rounded-3xl bg-slate-950/80 p-5">
                        <p className="text-xs uppercase tracking-[0.2em] text-slate-500">What To Do</p>
                        <p className="mt-3 text-sm leading-6 text-slate-200">{idea.whatToDo}</p>
                        <p className="mt-4 text-xs uppercase tracking-[0.2em] text-slate-500">Why It Was Picked</p>
                        <p className="mt-3 text-sm leading-6 text-slate-300">{idea.whyItWasPicked}</p>
                      </div>
                      <div className="rounded-3xl bg-slate-950/80 p-5">
                        <p className="text-xs uppercase tracking-[0.2em] text-slate-500">When To Sell</p>
                        <p className="mt-3 text-sm leading-6 text-slate-200">{idea.whenToSell}</p>
                        <p className="mt-4 text-xs uppercase tracking-[0.2em] text-slate-500">Beginner Tip</p>
                        <p className="mt-3 text-sm leading-6 text-slate-300">{idea.beginnerTip}</p>
                        <p className="mt-4 text-xs text-slate-500">
                          Last market update: {new Date(idea.lastUpdatedUtc).toLocaleString()}
                        </p>
                      </div>
                    </div>
                  </article>
                ))}
              </div>
            )}
          </section>
        )}

        {activeTab === 'penny' && (
          <section>
            <div className="rounded-[2rem] border border-slate-800 bg-gradient-to-br from-slate-900 via-slate-900 to-amber-950/40 p-6 shadow-lg shadow-slate-950/40">
              <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
                <div className="max-w-3xl">
                  <p className="text-sm uppercase tracking-[0.25em] text-amber-300/80">Penny Stocks</p>
                  <h2 className="mt-2 text-3xl font-semibold text-white">Best live low-priced setups for small-cap momentum</h2>
                  <p className="mt-3 text-sm leading-6 text-slate-300">
                    This tab scans low-priced stocks from the live market and shows only the names that still meet the penny-stock
                    price filter. These are higher-risk trades, so the plan is simple: wait for the entry, keep the stop loss tight,
                    and take profits quickly.
                  </p>
                </div>
                <button
                  onClick={fetchPennyStocks}
                  className="rounded-full bg-amber-300 px-5 py-2.5 text-sm font-semibold text-slate-950 transition hover:bg-amber-200"
                >
                  Refresh Penny Ideas
                </button>
              </div>

              {pennyStockBoard && (
                <div className="mt-6 grid gap-4 lg:grid-cols-3">
                  <div className="rounded-3xl border border-slate-800 bg-slate-950/70 p-4">
                    <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Timeframe</p>
                    <p className="mt-2 text-sm text-slate-200">{pennyStockBoard.timeframe}</p>
                  </div>
                  <div className="rounded-3xl border border-slate-800 bg-slate-950/70 p-4">
                    <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Market Note</p>
                    <p className="mt-2 text-sm text-slate-200">{pennyStockBoard.marketStatus}</p>
                  </div>
                  <div className="rounded-3xl border border-slate-800 bg-slate-950/70 p-4">
                    <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Risk Note</p>
                    <p className="mt-2 text-sm text-slate-200">{pennyStockBoard.beginnerNote}</p>
                  </div>
                </div>
              )}
            </div>

            {loading && <div className="mt-6 rounded-3xl bg-slate-900 p-8 text-center text-slate-400">Loading penny stock ideas...</div>}

            {!loading && error && <div className="mt-6 rounded-3xl bg-rose-950 p-6 text-slate-200">{error}</div>}

            {!loading && !error && (!pennyStockBoard || pennyStockBoard.picks.length === 0) && (
              <div className="mt-6 rounded-3xl bg-slate-900 p-8 text-center text-slate-400">
                No penny-stock setups are ready yet. The scanner is still filtering for sub-$10 names with enough momentum, so check back after a few more candles print.
              </div>
            )}

            {!loading && !error && pennyStockBoard && pennyStockBoard.picks.length > 0 && (
              <div className="mt-6 grid gap-5 xl:grid-cols-2">
                {pennyStockBoard.picks.map((idea, index) => (
                  <article key={idea.symbol} className="rounded-[2rem] border border-amber-500/20 bg-amber-950/10 p-6 shadow-lg shadow-slate-950/40">
                    <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
                      <div>
                        <p className="text-xs uppercase tracking-[0.25em] text-slate-500">Penny Pick {index + 1}</p>
                        <h3 className="mt-2 text-3xl font-semibold text-white">{idea.symbol}</h3>
                        <p className="mt-2 text-sm text-slate-300">{idea.setup}</p>
                      </div>
                      <div className="flex flex-wrap gap-2">
                        <span className="inline-flex rounded-full bg-amber-300/15 px-3 py-1 text-xs font-semibold text-amber-200">
                          BUY / MOMENTUM
                        </span>
                        <span className="inline-flex rounded-full bg-slate-950 px-3 py-1 text-xs font-semibold text-slate-300">
                          {idea.confidence}% confidence
                        </span>
                      </div>
                    </div>

                    <div className="mt-5 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                      <div className="rounded-3xl bg-slate-950/80 p-4">
                        <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Current</p>
                        <p className="mt-2 text-xl font-semibold text-white">{formatCurrency(idea.currentPrice)}</p>
                      </div>
                      <div className="rounded-3xl bg-slate-950/80 p-4">
                        <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Entry</p>
                        <p className="mt-2 text-xl font-semibold text-white">{formatCurrency(idea.entryPrice)}</p>
                      </div>
                      <div className="rounded-3xl bg-slate-950/80 p-4">
                        <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Stop Loss</p>
                        <p className="mt-2 text-xl font-semibold text-white">{formatCurrency(idea.stopLoss)}</p>
                      </div>
                      <div className="rounded-3xl bg-slate-950/80 p-4">
                        <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Reward / Risk</p>
                        <p className="mt-2 text-xl font-semibold text-white">{idea.riskRewardRatio.toFixed(2)}R</p>
                      </div>
                    </div>

                    <div className="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                      <div className="rounded-3xl border border-slate-800 bg-slate-950/60 p-4">
                        <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Target 1</p>
                        <p className="mt-2 text-lg font-semibold text-white">{formatCurrency(idea.targetPrice1)}</p>
                      </div>
                      <div className="rounded-3xl border border-slate-800 bg-slate-950/60 p-4">
                        <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Target 2</p>
                        <p className="mt-2 text-lg font-semibold text-white">{formatCurrency(idea.targetPrice2)}</p>
                      </div>
                      <div className="rounded-3xl border border-slate-800 bg-slate-950/60 p-4">
                        <p className="text-xs uppercase tracking-[0.2em] text-slate-500">RSI / Momentum</p>
                        <p className="mt-2 text-sm text-slate-200">RSI {idea.rsi.toFixed(2)} / {formatSignedPercent(idea.momentumPercent)}</p>
                      </div>
                      <div className="rounded-3xl border border-slate-800 bg-slate-950/60 p-4">
                        <p className="text-xs uppercase tracking-[0.2em] text-slate-500">Day Change / Volume</p>
                        <p className="mt-2 text-sm text-slate-200">{formatSignedPercent(idea.dayChangePercent)} / {idea.volume.toLocaleString()} shares</p>
                      </div>
                    </div>

                    <div className="mt-5 grid gap-4 lg:grid-cols-[1.15fr_1fr]">
                      <div className="rounded-3xl bg-slate-950/80 p-5">
                        <p className="text-xs uppercase tracking-[0.2em] text-slate-500">What To Do</p>
                        <p className="mt-3 text-sm leading-6 text-slate-200">{idea.whatToDo}</p>
                        <p className="mt-4 text-xs uppercase tracking-[0.2em] text-slate-500">Why It Was Picked</p>
                        <p className="mt-3 text-sm leading-6 text-slate-300">{idea.whyItWasPicked}</p>
                      </div>
                      <div className="rounded-3xl bg-slate-950/80 p-5">
                        <p className="text-xs uppercase tracking-[0.2em] text-slate-500">When To Sell</p>
                        <p className="mt-3 text-sm leading-6 text-slate-200">{idea.whenToSell}</p>
                        <p className="mt-4 text-xs uppercase tracking-[0.2em] text-slate-500">Beginner Tip</p>
                        <p className="mt-3 text-sm leading-6 text-slate-300">{idea.beginnerTip}</p>
                        <p className="mt-4 text-xs text-slate-500">
                          Last market update: {new Date(idea.lastUpdatedUtc).toLocaleString()}
                        </p>
                      </div>
                    </div>
                  </article>
                ))}
              </div>
            )}
          </section>
        )}

        {activeTab === 'analysis' && (
          <section>
            <div className="mb-4 flex items-center justify-between gap-4 rounded-3xl bg-slate-900 px-5 py-4 shadow-lg shadow-slate-950/30">
              <div>
                <p className="text-sm uppercase tracking-[0.2em] text-slate-500">Analysis</p>
                <h2 className="mt-2 text-2xl font-semibold">Live signal feed</h2>
              </div>
              <button
                onClick={fetchAnalysis}
                className="rounded-full bg-slate-200 px-4 py-2 text-slate-950 transition hover:bg-slate-300"
              >
                Refresh
              </button>
            </div>

            {loading && <div className="rounded-3xl bg-slate-900 p-8 text-center text-slate-400">Loading analysis...</div>}

            {!loading && error && <div className="rounded-3xl bg-rose-950 p-6 text-slate-200">{error}</div>}

            {!loading && !error && analysis.length === 0 && (
              <div className="rounded-3xl bg-slate-900 p-8 text-center text-slate-400">No analysis results yet.</div>
            )}

            {!loading && !error && analysis.length > 0 && (
              <div className="overflow-hidden rounded-3xl border border-slate-800 bg-slate-900 shadow-lg shadow-slate-950/40">
                <table className="w-full border-collapse text-left">
                  <thead className="bg-slate-950/80 text-slate-400">
                    <tr>
                      <th className="px-4 py-3 text-sm font-semibold">Symbol</th>
                      <th className="px-4 py-3 text-sm font-semibold">Price</th>
                      <th className="px-4 py-3 text-sm font-semibold">RSI</th>
                      <th className="px-4 py-3 text-sm font-semibold">Trend</th>
                      <th className="px-4 py-3 text-sm font-semibold">Action</th>
                      <th className="px-4 py-3 text-sm font-semibold">Confidence</th>
                      <th className="px-4 py-3 text-sm font-semibold">Reason</th>
                    </tr>
                  </thead>
                  <tbody>
                    {analysis.map((item) => (
                      <tr key={item.symbol} className="border-t border-slate-800 hover:bg-slate-950/80">
                        <td className="px-4 py-4 font-semibold text-white">{item.symbol}</td>
                        <td className="px-4 py-4 text-slate-300">${item.price.toFixed(2)}</td>
                        <td className="px-4 py-4 text-slate-300">{item.rsi.toFixed(1)}</td>
                        <td className="px-4 py-4">
                          <span className={`inline-flex rounded-full px-3 py-1 text-xs font-semibold ${
                            item.trend === 'up' ? 'bg-emerald-500/15 text-emerald-300' : 'bg-rose-500/15 text-rose-300'
                          }`}>
                            {item.trend.toUpperCase()}
                          </span>
                        </td>
                        <td className="px-4 py-4">
                          <span className={`inline-flex rounded-full px-3 py-1 text-xs font-semibold ${
                            item.action === 'buy_watch'
                              ? 'bg-emerald-500/15 text-emerald-300'
                              : item.action === 'sell_watch'
                              ? 'bg-rose-500/15 text-rose-300'
                              : 'bg-amber-500/15 text-amber-300'
                          }`}>
                            {item.action.replace(/_/g, ' ').toUpperCase()}
                          </span>
                        </td>
                        <td className="px-4 py-4 text-slate-300">{item.confidence}%</td>
                        <td className="px-4 py-4 text-slate-400">{item.reason}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        )}

        {activeTab === 'watchlists' && (
          <section>
            <div className="grid gap-6 xl:grid-cols-[0.9fr_1.1fr]">
              <div className="rounded-3xl border border-slate-800 bg-slate-900 p-6 shadow-lg shadow-slate-950/40">
                <h2 className="text-2xl font-semibold">Manage Watchlists</h2>
                <p className="mt-2 text-sm text-slate-400">
                  These symbols now drive the analysis feed, background price sync, and the live day-trading scanners.
                </p>

                <form onSubmit={submitWatchlist} className="mt-6 space-y-4">
                  <label className="block text-sm text-slate-300">
                    Watchlist
                    <select
                      value={watchlistForm.listType}
                      onChange={(event) =>
                        setWatchlistForm({
                          ...watchlistForm,
                          listType: event.target.value as WatchlistListType,
                        })
                      }
                      className="mt-2 w-full rounded-2xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                    >
                      {watchlistGroups.map((group) => (
                        <option key={group.key} value={group.key}>
                          {group.label}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label className="block text-sm text-slate-300">
                    Symbol
                    <input
                      required
                      value={watchlistForm.symbol}
                      onChange={(event) =>
                        setWatchlistForm({
                          ...watchlistForm,
                          symbol: event.target.value.toUpperCase(),
                        })
                      }
                      placeholder="AAPL"
                      className="mt-2 w-full rounded-2xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                    />
                  </label>

                  <button
                    type="submit"
                    className="inline-flex items-center justify-center rounded-full bg-emerald-500 px-6 py-3 text-sm font-semibold text-slate-950 transition hover:bg-emerald-400"
                  >
                    Add Symbol
                  </button>
                </form>

                <div className="mt-6 rounded-3xl border border-emerald-500/20 bg-emerald-950/20 p-4 text-sm text-emerald-100">
                  Changes flow into the scanners automatically. New symbols start using REST fallback immediately, then
                  join the live stream subscription as the backend refreshes its watchlist channels.
                </div>
              </div>

              <div className="space-y-5">
                {loading ? (
                  <div className="rounded-3xl bg-slate-900 p-8 text-center text-slate-400">Loading watchlists...</div>
                ) : (
                  watchlistGroups.map((group) => {
                    const entries = watchlists.filter((entry) => entry.listType === group.key);

                    return (
                      <div key={group.key} className="rounded-3xl border border-slate-800 bg-slate-900 p-6 shadow-lg shadow-slate-950/40">
                        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                          <div>
                            <h3 className="text-xl font-semibold text-white">{group.label}</h3>
                            <p className="mt-2 text-sm text-slate-400">{group.description}</p>
                          </div>
                          <div className="rounded-full bg-slate-950 px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em] text-slate-300">
                            {entries.length} symbols
                          </div>
                        </div>

                        {entries.length === 0 ? (
                          <div className="mt-5 rounded-3xl bg-slate-950 p-5 text-sm text-slate-400">
                            No symbols configured for this watchlist yet.
                          </div>
                        ) : (
                          <div className="mt-5 flex flex-wrap gap-3">
                            {entries.map((entry) => (
                              <div
                                key={entry.id}
                                className="flex items-center gap-3 rounded-full border border-slate-700 bg-slate-950 px-4 py-3"
                              >
                                <div>
                                  <p className="text-sm font-semibold text-white">{entry.symbol}</p>
                                  <p className="text-xs text-slate-500">Order {entry.sortOrder + 1}</p>
                                </div>
                                <button
                                  type="button"
                                  onClick={() => deleteWatchlistEntry(entry.id)}
                                  className="rounded-full bg-rose-500 px-3 py-1.5 text-xs font-semibold text-slate-950 transition hover:bg-rose-400"
                                >
                                  Remove
                                </button>
                              </div>
                            ))}
                          </div>
                        )}
                      </div>
                    );
                  })
                )}
              </div>
            </div>
          </section>
        )}

        {activeTab === 'portfolio' && (
          <section>
            <div className="grid gap-6 lg:grid-cols-[1.2fr_1fr]">
              <div className="rounded-3xl border border-slate-800 bg-slate-900 p-6 shadow-lg shadow-slate-950/40">
                <h2 className="text-2xl font-semibold">Add Portfolio Position</h2>
                <p className="mt-2 text-sm text-slate-400">Track your open positions and risk parameters.</p>

                <form onSubmit={submitPortfolio} className="mt-6 space-y-4">
                  <div className="grid gap-4 sm:grid-cols-2">
                    <label className="block text-sm text-slate-300">
                      Symbol
                      <input
                        required
                        value={portfolioForm.symbol}
                        onChange={(e) => setPortfolioForm({ ...portfolioForm, symbol: e.target.value.toUpperCase() })}
                        className="mt-2 w-full rounded-2xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                      />
                    </label>
                    <label className="block text-sm text-slate-300">
                      Quantity
                      <input
                        type="number"
                        min="0"
                        step="any"
                        required
                        value={portfolioForm.quantity}
                        onChange={(e) => setPortfolioForm({ ...portfolioForm, quantity: Number(e.target.value) })}
                        className="mt-2 w-full rounded-2xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                      />
                    </label>
                    <label className="block text-sm text-slate-300">
                      Entry Price
                      <input
                        type="number"
                        min="0"
                        step="any"
                        required
                        value={portfolioForm.entryPrice}
                        onChange={(e) => setPortfolioForm({ ...portfolioForm, entryPrice: Number(e.target.value) })}
                        className="mt-2 w-full rounded-2xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                      />
                    </label>
                    <label className="block text-sm text-slate-300">
                      Entry Date
                      <input
                        type="date"
                        required
                        value={portfolioForm.entryDate}
                        onChange={(e) => setPortfolioForm({ ...portfolioForm, entryDate: e.target.value })}
                        className="mt-2 w-full rounded-2xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                      />
                    </label>
                  </div>

                  <div className="grid gap-4 sm:grid-cols-2">
                    <label className="block text-sm text-slate-300">
                      Stop Loss
                      <input
                        type="number"
                        min="0"
                        step="any"
                        value={portfolioForm.stopLoss}
                        onChange={(e) => setPortfolioForm({ ...portfolioForm, stopLoss: e.target.value })}
                        className="mt-2 w-full rounded-2xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                      />
                    </label>
                    <label className="block text-sm text-slate-300">
                      Take Profit
                      <input
                        type="number"
                        min="0"
                        step="any"
                        value={portfolioForm.takeProfit}
                        onChange={(e) => setPortfolioForm({ ...portfolioForm, takeProfit: e.target.value })}
                        className="mt-2 w-full rounded-2xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                      />
                    </label>
                  </div>

                  <label className="block text-sm text-slate-300">
                    Notes
                    <textarea
                      value={portfolioForm.notes}
                      onChange={(e) => setPortfolioForm({ ...portfolioForm, notes: e.target.value })}
                      className="mt-2 w-full rounded-3xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                      rows={4}
                    />
                  </label>

                  <button
                    type="submit"
                    className="inline-flex items-center justify-center rounded-full bg-emerald-500 px-6 py-3 text-sm font-semibold text-slate-950 transition hover:bg-emerald-400"
                  >
                    Add Position
                  </button>
                </form>
              </div>

              <div className="rounded-3xl border border-slate-800 bg-slate-900 p-6 shadow-lg shadow-slate-950/40">
                <h2 className="text-2xl font-semibold">Open Portfolio</h2>
                <p className="mt-2 text-sm text-slate-400">Manage your tracked holdings and exit alerts.</p>

                {loading ? (
                  <div className="mt-6 rounded-3xl bg-slate-950 p-6 text-center text-slate-400">Loading portfolio...</div>
                ) : portfolio.length === 0 ? (
                  <div className="mt-6 rounded-3xl bg-slate-950 p-6 text-center text-slate-400">No active positions found.</div>
                ) : (
                  <div className="mt-6 space-y-4">
                    {portfolio.map((position) => (
                      <div key={position.id} className="rounded-3xl border border-slate-800 bg-slate-950 p-4">
                        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                          <div>
                            <p className="text-sm text-slate-400">{position.symbol}</p>
                            <p className="text-xl font-semibold text-white">{position.quantity} shares</p>
                          </div>
                          <button
                            onClick={() => deletePortfolioPosition(position.id)}
                            className="rounded-full bg-rose-500 px-4 py-2 text-sm font-semibold text-slate-950 transition hover:bg-rose-400"
                          >
                            Remove
                          </button>
                        </div>
                        <div className="mt-4 grid gap-2 sm:grid-cols-2">
                          <div className="text-sm text-slate-400">Entry price: ${position.entryPrice.toFixed(2)}</div>
                          <div className="text-sm text-slate-400">Date: {new Date(position.entryDate).toLocaleDateString()}</div>
                          <div className="text-sm text-slate-400">Stop loss: {position.stopLoss ? `$${position.stopLoss.toFixed(2)}` : 'N/A'}</div>
                          <div className="text-sm text-slate-400">Take profit: {position.takeProfit ? `$${position.takeProfit.toFixed(2)}` : 'N/A'}</div>
                        </div>
                        {position.notes && <p className="mt-4 text-sm text-slate-300">Notes: {position.notes}</p>}
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </div>
          </section>
        )}

        {activeTab === 'alerts' && (
          <section>
            <div className="grid gap-6 lg:grid-cols-[1.2fr_1fr]">
              <div className="rounded-3xl border border-slate-800 bg-slate-900 p-6 shadow-lg shadow-slate-950/40">
                <div className="mb-4 flex items-center justify-between gap-4">
                  <div>
                    <h2 className="text-2xl font-semibold">{editingAlertId ? 'Edit Alert' : 'Create Alert'}</h2>
                    <p className="mt-2 text-sm text-slate-400">Receive notifications when market conditions are met.</p>
                  </div>
                  <div className="flex gap-2">
                    {editingAlertId && (
                      <button
                        type="button"
                        onClick={cancelEditAlert}
                        className="rounded-full bg-slate-700 px-4 py-2 text-sm font-semibold text-slate-100 transition hover:bg-slate-600"
                      >
                        Cancel
                      </button>
                    )}
                    <button
                      type="button"
                      onClick={fetchAlerts}
                      className="rounded-full bg-slate-200 px-4 py-2 text-sm font-semibold text-slate-950 transition hover:bg-slate-300"
                    >
                      Refresh
                    </button>
                  </div>
                </div>

                <form onSubmit={submitAlert} className="mt-6 space-y-4">
                  <div className="grid gap-4 sm:grid-cols-2">
                    <label className="block text-sm text-slate-300">
                      Symbol
                      <input
                        required
                        value={alertForm.symbol}
                        onChange={(e) => setAlertForm({ ...alertForm, symbol: e.target.value.toUpperCase() })}
                        className="mt-2 w-full rounded-2xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                      />
                    </label>
                    <label className="block text-sm text-slate-300">
                      Alert Type
                      <select
                        required
                        value={alertForm.alertType}
                        onChange={(e) => setAlertForm({ ...alertForm, alertType: e.target.value })}
                        className="mt-2 w-full rounded-2xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                      >
                        <option value="price_above">Price Above</option>
                        <option value="price_below">Price Below</option>
                        <option value="rsi_above">RSI Above</option>
                        <option value="rsi_below">RSI Below</option>
                      </select>
                    </label>
                  </div>

                  <div className="grid gap-4 sm:grid-cols-2">
                    <label className="block text-sm text-slate-300">
                      Threshold
                      <input
                        type="number"
                        min="0"
                        step="any"
                        required
                        value={alertForm.threshold}
                        onChange={(e) => setAlertForm({ ...alertForm, threshold: Number(e.target.value) })}
                        className="mt-2 w-full rounded-2xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                      />
                    </label>
                    <label className="block text-sm text-slate-300">
                      Condition
                      <input
                        value={alertForm.condition}
                        onChange={(e) => setAlertForm({ ...alertForm, condition: e.target.value })}
                        className="mt-2 w-full rounded-2xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                      />
                    </label>
                  </div>

                  <label className="block text-sm text-slate-300">
                    Expires At
                    <input
                      type="date"
                      value={alertForm.expiresAt}
                      onChange={(e) => setAlertForm({ ...alertForm, expiresAt: e.target.value })}
                      className="mt-2 w-full rounded-2xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                    />
                  </label>

                  <button
                    type="submit"
                    className="inline-flex items-center justify-center rounded-full bg-emerald-500 px-6 py-3 text-sm font-semibold text-slate-950 transition hover:bg-emerald-400"
                  >
                    {editingAlertId ? 'Save Alert' : 'Create Alert'}
                  </button>
                </form>
              </div>

              <div className="rounded-3xl border border-slate-800 bg-slate-900 p-6 shadow-lg shadow-slate-950/40">
                <h2 className="text-2xl font-semibold">Alerts Overview</h2>
                <p className="mt-2 text-sm text-slate-400">Monitor active and triggered alerts in one place.</p>

                {loading ? (
                  <div className="mt-6 rounded-3xl bg-slate-950 p-6 text-center text-slate-400">Loading alerts...</div>
                ) : alerts.length === 0 ? (
                  <div className="mt-6 rounded-3xl bg-slate-950 p-6 text-center text-slate-400">No alerts configured yet.</div>
                ) : (
                  <div className="mt-6 space-y-8">
                    <div>
                      <h3 className="text-xl font-semibold text-white">Active Alerts</h3>
                      {activeAlerts.length === 0 ? (
                        <div className="mt-4 rounded-3xl bg-slate-950 p-6 text-sm text-slate-400">No active alerts.</div>
                      ) : (
                        <div className="mt-4 space-y-4">
                          {activeAlerts.map((alert) => (
                            <div key={alert.id} className="rounded-3xl border border-slate-800 bg-slate-950 p-4">
                              <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                                <div>
                                  <p className="text-sm text-slate-400">{alert.symbol}</p>
                                  <p className="text-xl font-semibold text-white">{alert.alertType.replace(/_/g, ' ').toUpperCase()}</p>
                                </div>
                                <div className="flex flex-wrap gap-2">
                                  <button
                                    type="button"
                                    onClick={() => startEditAlert(alert)}
                                    className="rounded-full bg-slate-200 px-4 py-2 text-sm font-semibold text-slate-950 transition hover:bg-slate-300"
                                  >
                                    Edit
                                  </button>
                                  <button
                                    onClick={() => deleteAlert(alert.id)}
                                    className="rounded-full bg-rose-500 px-4 py-2 text-sm font-semibold text-slate-950 transition hover:bg-rose-400"
                                  >
                                    Remove
                                  </button>
                                </div>
                              </div>
                              <div className="mt-4 grid gap-2 sm:grid-cols-2 text-sm text-slate-400">
                                <div>Threshold: {alert.threshold}</div>
                                <div>Condition: {alert.condition || 'None'}</div>
                                <div>Status: {alert.isActive ? 'Active' : 'Inactive'}</div>
                                <div>Triggered: {alert.isTriggered ? 'Yes' : 'No'}</div>
                              </div>
                              <div className="mt-3 text-sm text-slate-400">
                                Expires: {alert.expiresAt ? new Date(alert.expiresAt).toLocaleDateString() : 'Never'}
                              </div>
                            </div>
                          ))}
                        </div>
                      )}
                    </div>

                    <div>
                      <h3 className="text-xl font-semibold text-white">Triggered Alerts</h3>
                      {triggeredAlerts.length === 0 ? (
                        <div className="mt-4 rounded-3xl bg-slate-950 p-6 text-sm text-slate-400">No triggered alerts yet.</div>
                      ) : (
                        <div className="mt-4 space-y-4">
                          {triggeredAlerts.map((alert) => (
                            <div key={alert.id} className="rounded-3xl border border-slate-800 bg-slate-950 p-4">
                              <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                                <div>
                                  <p className="text-sm text-slate-400">{alert.symbol}</p>
                                  <p className="text-xl font-semibold text-white">{alert.alertType.replace(/_/g, ' ').toUpperCase()}</p>
                                </div>
                                <div className="flex flex-wrap gap-2">
                                  <button
                                    type="button"
                                    onClick={() => startEditAlert(alert)}
                                    className="rounded-full bg-slate-200 px-4 py-2 text-sm font-semibold text-slate-950 transition hover:bg-slate-300"
                                  >
                                    Edit
                                  </button>
                                  <button
                                    onClick={() => deleteAlert(alert.id)}
                                    className="rounded-full bg-rose-500 px-4 py-2 text-sm font-semibold text-slate-950 transition hover:bg-rose-400"
                                  >
                                    Remove
                                  </button>
                                </div>
                              </div>
                              <div className="mt-4 grid gap-2 sm:grid-cols-2 text-sm text-slate-400">
                                <div>Threshold: {alert.threshold}</div>
                                <div>Condition: {alert.condition || 'None'}</div>
                                <div>Triggered At: {alert.triggeredAt ? new Date(alert.triggeredAt).toLocaleString() : 'Unknown'}</div>
                                <div>Expires: {alert.expiresAt ? new Date(alert.expiresAt).toLocaleDateString() : 'Never'}</div>
                              </div>
                            </div>
                          ))}
                        </div>
                      )}
                    </div>
                  </div>
                )}
              </div>
            </div>
          </section>
        )}

        {activeTab === 'trades' && (
          <section>
            <div className="grid gap-6 lg:grid-cols-[1.2fr_1fr]">
              <div className="rounded-3xl border border-slate-800 bg-slate-900 p-6 shadow-lg shadow-slate-950/40">
                <h2 className="text-2xl font-semibold">Log Trade</h2>
                <p className="mt-2 text-sm text-slate-400">Capture entry and exit details for each trade.</p>

                <form onSubmit={submitTrade} className="mt-6 space-y-4">
                  <div className="grid gap-4 sm:grid-cols-2">
                    <label className="block text-sm text-slate-300">
                      Symbol
                      <input
                        required
                        value={tradeForm.symbol}
                        onChange={(e) => setTradeForm({ ...tradeForm, symbol: e.target.value.toUpperCase() })}
                        className="mt-2 w-full rounded-2xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                      />
                    </label>
                    <label className="block text-sm text-slate-300">
                      Side
                      <select
                        required
                        value={tradeForm.side}
                        onChange={(e) => setTradeForm({ ...tradeForm, side: e.target.value })}
                        className="mt-2 w-full rounded-2xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                      >
                        <option value="buy">Buy</option>
                        <option value="sell">Sell</option>
                      </select>
                    </label>
                    <label className="block text-sm text-slate-300">
                      Quantity
                      <input
                        type="number"
                        min="0"
                        step="any"
                        required
                        value={tradeForm.quantity}
                        onChange={(e) => setTradeForm({ ...tradeForm, quantity: Number(e.target.value) })}
                        className="mt-2 w-full rounded-2xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                      />
                    </label>
                    <label className="block text-sm text-slate-300">
                      Entry Price
                      <input
                        type="number"
                        min="0"
                        step="any"
                        required
                        value={tradeForm.entryPrice}
                        onChange={(e) => setTradeForm({ ...tradeForm, entryPrice: Number(e.target.value) })}
                        className="mt-2 w-full rounded-2xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                      />
                    </label>
                  </div>

                  <div className="grid gap-4 sm:grid-cols-2">
                    <label className="block text-sm text-slate-300">
                      Entry Date
                      <input
                        type="date"
                        required
                        value={tradeForm.entryDate}
                        onChange={(e) => setTradeForm({ ...tradeForm, entryDate: e.target.value })}
                        className="mt-2 w-full rounded-2xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                      />
                    </label>
                    <label className="block text-sm text-slate-300">
                      Exit Price
                      <input
                        type="number"
                        min="0"
                        step="any"
                        value={tradeForm.exitPrice}
                        onChange={(e) => setTradeForm({ ...tradeForm, exitPrice: e.target.value })}
                        className="mt-2 w-full rounded-2xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                      />
                    </label>
                  </div>

                  <div className="grid gap-4 sm:grid-cols-2">
                    <label className="block text-sm text-slate-300">
                      PnL
                      <input
                        type="number"
                        step="any"
                        value={tradeForm.pnl}
                        onChange={(e) => setTradeForm({ ...tradeForm, pnl: e.target.value })}
                        className="mt-2 w-full rounded-2xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                      />
                    </label>
                    <label className="block text-sm text-slate-300">
                      PnL %
                      <input
                        type="number"
                        step="any"
                        value={tradeForm.pnlPercent}
                        onChange={(e) => setTradeForm({ ...tradeForm, pnlPercent: e.target.value })}
                        className="mt-2 w-full rounded-2xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                      />
                    </label>
                  </div>

                  <label className="block text-sm text-slate-300">
                    Exit Date
                    <input
                      type="date"
                      value={tradeForm.exitDate}
                      onChange={(e) => setTradeForm({ ...tradeForm, exitDate: e.target.value })}
                      className="mt-2 w-full rounded-2xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                    />
                  </label>

                  <label className="block text-sm text-slate-300">
                    Exit Reason
                    <textarea
                      value={tradeForm.exitReason}
                      onChange={(e) => setTradeForm({ ...tradeForm, exitReason: e.target.value })}
                      className="mt-2 w-full rounded-3xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                      rows={3}
                    />
                  </label>

                  <label className="block text-sm text-slate-300">
                    Notes
                    <textarea
                      value={tradeForm.notes}
                      onChange={(e) => setTradeForm({ ...tradeForm, notes: e.target.value })}
                      className="mt-2 w-full rounded-3xl border border-slate-700 bg-slate-950 px-4 py-3 text-slate-100 outline-none focus:border-slate-500"
                      rows={3}
                    />
                  </label>

                  <button
                    type="submit"
                    className="inline-flex items-center justify-center rounded-full bg-emerald-500 px-6 py-3 text-sm font-semibold text-slate-950 transition hover:bg-emerald-400"
                  >
                    Save Trade
                  </button>
                </form>
              </div>

              <div className="rounded-3xl border border-slate-800 bg-slate-900 p-6 shadow-lg shadow-slate-950/40">
                <h2 className="text-2xl font-semibold">Trade Log</h2>
                <p className="mt-2 text-sm text-slate-400">Review trade performance and history.</p>

                {loading ? (
                  <div className="mt-6 rounded-3xl bg-slate-950 p-6 text-center text-slate-400">Loading trades...</div>
                ) : trades.length === 0 ? (
                  <div className="mt-6 rounded-3xl bg-slate-950 p-6 text-center text-slate-400">No trades logged yet.</div>
                ) : (
                  <div className="mt-6 space-y-4">
                    {trades.map((trade) => (
                      <div key={trade.id} className="rounded-3xl border border-slate-800 bg-slate-950 p-4">
                        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                          <div>
                            <p className="text-sm text-slate-400">{trade.symbol} / {trade.side.toUpperCase()}</p>
                            <p className="text-xl font-semibold text-white">{trade.quantity} @ ${trade.entryPrice.toFixed(2)}</p>
                          </div>
                          <button
                            onClick={() => deleteTrade(trade.id)}
                            className="rounded-full bg-rose-500 px-4 py-2 text-sm font-semibold text-slate-950 transition hover:bg-rose-400"
                          >
                            Remove
                          </button>
                        </div>
                        <div className="mt-4 grid gap-2 sm:grid-cols-2">
                          <div className="text-sm text-slate-400">Entry: {new Date(trade.entryDate).toLocaleDateString()}</div>
                          <div className="text-sm text-slate-400">Exit: {trade.exitDate ? new Date(trade.exitDate).toLocaleDateString() : 'Open'}</div>
                          <div className="text-sm text-slate-400">PnL: {trade.pnl !== null && trade.pnl !== undefined ? `$${trade.pnl.toFixed(2)}` : 'N/A'}</div>
                          <div className="text-sm text-slate-400">PnL %: {trade.pnlPercent !== null && trade.pnlPercent !== undefined ? `${trade.pnlPercent.toFixed(2)}%` : 'N/A'}</div>
                        </div>
                        {trade.exitReason && <p className="mt-4 text-sm text-slate-300">Exit: {trade.exitReason}</p>}
                        {trade.notes && <p className="mt-2 text-sm text-slate-300">Notes: {trade.notes}</p>}
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </div>
          </section>
        )}
      </div>
    </div>
  );
}
