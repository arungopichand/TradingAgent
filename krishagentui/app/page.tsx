'use client';

import { useState, useEffect } from 'react';

interface AnalysisResult {
  symbol: string;
  price: number;
  rsi: number;
  trend: string;
  action: string;
  confidence: number;
  reason: string;
}

export default function Home() {
  const [data, setData] = useState<AnalysisResult[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [lastUpdated, setLastUpdated] = useState<string | null>(null);

  useEffect(() => {
    const fetchAnalysis = async () => {
      try {
        setLoading(true);
        setError(null);
        const response = await fetch('http://localhost:5220/api/trade/analyze');

        if (!response.ok) {
          throw new Error(`API error: ${response.status}`);
        }

        const result = await response.json();
        
        // Handle both array and object responses
        const analysisData = Array.isArray(result) ? result : [result];
        setData(analysisData);
        setLastUpdated(new Date().toLocaleTimeString());
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to fetch analysis data');
        setData([]);
      } finally {
        setLoading(false);
      }
    };

    // Initial fetch
    fetchAnalysis();

    // Auto-refresh every 5 minutes (300000 ms)
    const intervalId = setInterval(fetchAnalysis, 300000);

    // Cleanup interval on unmount
    return () => clearInterval(intervalId);
  }, []);

  const getActionColor = (action: string): string => {
    switch (action.toLowerCase()) {
      case 'buy_watch':
        return 'text-green-400';
      case 'sell_watch':
        return 'text-red-400';
      case 'hold':
        return 'text-yellow-400';
      default:
        return 'text-gray-400';
    }
  };

  const getActionBgColor = (action: string): string => {
    switch (action.toLowerCase()) {
      case 'buy_watch':
        return 'bg-green-900/20';
      case 'sell_watch':
        return 'bg-red-900/20';
      case 'hold':
        return 'bg-yellow-900/20';
      default:
        return 'bg-gray-900/20';
    }
  };

  return (
    <div className="min-h-screen" style={{ backgroundColor: '#0B0F19' }}>
      <div className="max-w-7xl mx-auto px-4 py-8">
        {/* Header */}
        <div className="mb-8 flex justify-between items-start">
          <div>
            <h1 className="text-4xl font-bold text-white mb-2">Krish Agent</h1>
            <p className="text-gray-400">AI-Powered Trading Analysis Dashboard</p>
          </div>
          {lastUpdated && (
            <div className="text-right">
              <p className="text-gray-400 text-sm">Last Updated:</p>
              <p className="text-white font-mono text-lg">{lastUpdated}</p>
            </div>
          )}
        </div>

        {/* Loading State */}
        {loading && (
          <div className="flex items-center justify-center py-12">
            <div className="text-center">
              <div className="inline-block animate-spin">
                <div className="h-12 w-12 border-4 border-gray-600 border-t-blue-400 rounded-full"></div>
              </div>
              <p className="text-gray-400 mt-4 text-lg">Loading signals...</p>
            </div>
          </div>
        )}

        {/* Error State */}
        {error && !loading && (
          <div className="bg-red-950/40 border border-red-700/50 rounded-lg p-4 mb-6">
            <p className="text-red-400 text-sm">{error}</p>
          </div>
        )}

        {/* Table */}
        {!loading && !error && data.length > 0 && (
          <div className="overflow-hidden rounded-lg border border-gray-700/50 shadow-lg">
            <table className="w-full">
              {/* Table Header */}
              <thead>
                <tr className="border-b border-gray-700/50" style={{ backgroundColor: '#111827' }}>
                  <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">Symbol</th>
                  <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">Price</th>
                  <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">RSI</th>
                  <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">Trend</th>
                  <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">Action</th>
                  <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">Confidence</th>
                  <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">Reason</th>
                </tr>
              </thead>

              {/* Table Body */}
              <tbody>
                {data.map((item, index) => (
                  <tr
                    key={index}
                    className="border-b border-gray-700/30 hover:bg-gray-800/40 transition-all duration-200 ease-in-out"
                  >
                    {/* Symbol */}
                    <td className="px-6 py-4">
                      <span className="font-semibold text-white">{item.symbol}</span>
                    </td>

                    {/* Price */}
                    <td className="px-6 py-4">
                      <span className="text-gray-300">${item.price?.toFixed(2) || 'N/A'}</span>
                    </td>

                    {/* RSI */}
                    <td className="px-6 py-4">
                      <span className="text-gray-300">{item.rsi?.toFixed(2) || 'N/A'}</span>
                    </td>

                    {/* Trend */}
                    <td className="px-6 py-4">
                      <span
                        className={`px-3 py-1 rounded-full text-sm font-medium ${
                          item.trend === 'up'
                            ? 'text-green-400 bg-green-900/20'
                            : 'text-red-400 bg-red-900/20'
                        }`}
                      >
                        {item.trend?.toUpperCase() || 'N/A'}
                      </span>
                    </td>

                    {/* Action */}
                    <td className="px-6 py-4 font-bold">
                      <span
                        className={`px-3 py-1 rounded-full text-sm font-bold ${getActionColor(
                          item.action
                        )} ${getActionBgColor(item.action)}`}
                      >
                        {item.action?.replace(/_/g, ' ').toUpperCase() || 'N/A'}
                      </span>
                    </td>

                    {/* Confidence */}
                    <td className="px-6 py-4">
                      <div className="flex items-center">
                        <div className="w-16 bg-gray-700 rounded-full h-2 mr-2">
                          <div
                            className="bg-blue-500 h-2 rounded-full"
                            style={{ width: `${item.confidence || 0}%` }}
                          ></div>
                        </div>
                        <span className="text-gray-300 text-sm">{item.confidence || 0}%</span>
                      </div>
                    </td>

                    {/* Reason */}
                    <td className="px-6 py-4">
                      <span className="text-gray-400 text-sm">{item.reason || 'N/A'}</span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Empty State */}
        {!loading && !error && data.length === 0 && (
          <div className="text-center py-12">
            <p className="text-gray-500 text-lg">No data available</p>
          </div>
        )}
      </div>
    </div>
  );
}
