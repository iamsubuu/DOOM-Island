import React, { useMemo, useState } from 'react';
import {
  Check,
  Code2,
  Copy,
  FileText,
  Image as ImageIcon,
  Link2,
  Pin,
  Search,
  Trash2,
} from 'lucide-react';
import { useNova } from '../../context/NovaContext';
import type { ClipboardItem, ClipboardItemType } from '../../types/island';
import { SafeMediaImage } from '../ui/Controls';

type ClipboardFilter = 'all' | 'pinned' | ClipboardItemType;

export function ClipboardPanel() {
  const {
    clipboardItems,
    copiedId,
    copyClipboardItem,
    togglePinClipboard,
    deleteClipboardItem,
    clearClipboardHistory,
  } = useNova();

  const [query, setQuery] = useState('');
  const [filter, setFilter] = useState<ClipboardFilter>('all');

  const filteredItems = useMemo(() => {
    const q = query.trim().toLowerCase();
    const list = clipboardItems.filter((item) => {
      if (filter === 'pinned' && !item.pinned) return false;
      if (filter !== 'all' && filter !== 'pinned' && item.type !== filter) return false;
      if (!q) return true;
      return (
        item.content.toLowerCase().includes(q) ||
        (item.previewTitle?.toLowerCase().includes(q) ?? false) ||
        item.sourceApp.toLowerCase().includes(q)
      );
    });
    // Sort pinned items first
    return [...list].sort((a, b) => Number(b.pinned) - Number(a.pinned));
  }, [clipboardItems, query, filter]);

  const getTypeIcon = (type: ClipboardItem['type']) => {
    switch (type) {
      case 'code':
        return <Code2 className="w-3.5 h-3.5 text-zinc-400" />;
      case 'link':
        return <Link2 className="w-3.5 h-3.5 text-zinc-400" />;
      case 'image':
        return <ImageIcon className="w-3.5 h-3.5 text-zinc-400" />;
      default:
        return <FileText className="w-3.5 h-3.5 text-zinc-400" />;
    }
  };

  return (
    <div className="flex flex-col gap-3">
      {/* Search & Clear Bar */}
      <div className="flex items-center gap-2">
        <div className="relative flex-1">
          <Search className="w-3.5 h-3.5 text-zinc-500 absolute left-2.5 top-1/2 -translate-y-1/2" />
          <input
            type="text"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Search clipboard history..."
            className="w-full bg-white/[0.05] border border-white/[0.08] rounded-lg pl-8 pr-3 py-1.5 text-[12px] text-white placeholder:text-zinc-500 focus:outline-none focus:border-white/25"
          />
        </div>
        <button
          type="button"
          onClick={() => clearClipboardHistory(true)}
          className="px-2.5 py-1.5 rounded-lg text-[11.5px] font-medium text-zinc-400 hover:text-zinc-100 bg-white/[0.04] hover:bg-white/[0.09] transition-colors whitespace-nowrap shrink-0 cursor-pointer"
          title="Clear unpinned items"
        >
          Clear Unpinned
        </button>
      </div>

      {/* Filter Segmented Control */}
      <div className="flex items-center gap-1 overflow-x-auto nova-scroll">
        {(
          [
            ['all', 'All'],
            ['pinned', 'Pinned'],
            ['text', 'Text'],
            ['code', 'Code'],
            ['link', 'Links'],
            ['image', 'Images'],
          ] as [ClipboardFilter, string][]
        ).map(([id, label]) => (
          <button
            key={id}
            type="button"
            onClick={() => setFilter(id)}
            className={`px-2.5 py-1 rounded-md text-[11.5px] font-medium transition-colors whitespace-nowrap shrink-0 cursor-pointer ${
              filter === id
                ? 'bg-white/[0.12] text-white'
                : 'text-zinc-400 hover:text-zinc-200 hover:bg-white/[0.05]'
            }`}
          >
            {label}
          </button>
        ))}
      </div>

      {/* Entries List */}
      <div className="max-h-56 overflow-y-auto nova-scroll divide-y divide-white/[0.06] pr-0.5">
        {filteredItems.length === 0 ? (
          <div className="py-8 text-center text-[12px] text-zinc-500">
            No clipboard items match your filter.
          </div>
        ) : (
          filteredItems.map((item) => {
            const isCopied = copiedId === item.id;
            return (
              <div
                key={item.id}
                className="py-2.5 px-1.5 flex items-start justify-between gap-3 hover:bg-white/[0.03] rounded-lg transition-colors group"
              >
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2 text-[11px] text-zinc-400 mb-1">
                    {getTypeIcon(item.type)}
                    <span className="font-medium text-zinc-200 truncate">
                      {item.previewTitle || item.type.toUpperCase()}
                    </span>
                    <span aria-hidden="true">·</span>
                    <span className="truncate">{item.sourceApp}</span>
                    <span aria-hidden="true">·</span>
                    <span className="shrink-0">{item.timestamp}</span>
                  </div>

                  {item.type === 'image' ? (
                    <div className="flex items-center gap-3 mt-1.5">
                      <SafeMediaImage
                        src={item.imageUrl}
                        alt={item.previewTitle || 'Clipboard image'}
                        fallbackKind="preview"
                        className="w-20 h-12 rounded-md object-cover border border-white/10 shrink-0"
                      />
                      <span className="text-[11.5px] text-zinc-400 font-mono-tabular">
                        {item.content}
                      </span>
                    </div>
                  ) : item.type === 'code' ? (
                    <pre className="mt-1 px-2.5 py-1.5 rounded-md bg-white/[0.04] text-[11px] font-mono-tabular text-zinc-300 overflow-x-auto max-h-14 leading-relaxed">
                      {item.content}
                    </pre>
                  ) : (
                    <p
                      className={`text-[12px] line-clamp-2 leading-relaxed ${
                        item.type === 'link' ? 'text-sky-300/90 underline underline-offset-2' : 'text-zinc-300'
                      }`}
                    >
                      {item.content}
                    </p>
                  )}
                </div>

                {/* Row Actions */}
                <div className="flex items-center gap-1 shrink-0 pt-0.5">
                  <button
                    type="button"
                    onClick={() => copyClipboardItem(item)}
                    className={`p-1.5 rounded-md transition-colors cursor-pointer ${
                      isCopied
                        ? 'text-emerald-400 bg-emerald-500/10'
                        : 'text-zinc-400 hover:text-white hover:bg-white/[0.08]'
                    }`}
                    title="Copy again"
                    aria-label="Copy item"
                  >
                    {isCopied ? <Check className="w-3.5 h-3.5" /> : <Copy className="w-3.5 h-3.5" />}
                  </button>
                  <button
                    type="button"
                    onClick={() => togglePinClipboard(item.id)}
                    className={`p-1.5 rounded-md transition-colors cursor-pointer ${
                      item.pinned
                        ? 'text-white bg-white/[0.10]'
                        : 'text-zinc-500 hover:text-zinc-200 hover:bg-white/[0.08]'
                    }`}
                    title={item.pinned ? 'Unpin item' : 'Pin item'}
                    aria-label={item.pinned ? 'Unpin item' : 'Pin item'}
                  >
                    <Pin className="w-3.5 h-3.5" />
                  </button>
                  <button
                    type="button"
                    onClick={() => deleteClipboardItem(item.id)}
                    className="p-1.5 rounded-md text-zinc-500 hover:text-rose-400 hover:bg-white/[0.08] transition-colors cursor-pointer"
                    title="Delete item"
                    aria-label="Delete item"
                  >
                    <Trash2 className="w-3.5 h-3.5" />
                  </button>
                </div>
              </div>
            );
          })
        )}
      </div>
    </div>
  );
}
