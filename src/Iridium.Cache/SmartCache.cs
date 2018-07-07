#region License
//=============================================================================
// Iridium-Core - Portable .NET Productivity Library 
//
// Copyright (c) 2008-2017 Philippe Leybaert
//
// Permission is hereby granted, free of charge, to any person obtaining a copy 
// of this software and associated documentation files (the "Software"), to deal 
// in the Software without restriction, including without limitation the rights 
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
// copies of the Software, and to permit persons to whom the Software is 
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
// IN THE SOFTWARE.
//=============================================================================
#endregion

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

[assembly: InternalsVisibleTo("Iridium.Cache.Test")]

namespace Iridium.Cache
{
    public class SmartCache<T>
    {
        public TimeSpan DefaultSlidingExpiration { get; set; } = TimeSpan.FromDays(1000);
        public DateTime DefaultAbsoluteExpiration { get; set; } = DateTime.MaxValue;

        private static DateTime Min(DateTime t1, DateTime t2)
        {
            return t1 < t2 ? t1 : t2;
        }

        private class CachedItem
        {
            public readonly string Key;
            public readonly T Value;
            public DateTime ExpirationTime;
            private readonly DateTime _absoluteExpiration;
            private readonly TimeSpan _slidingExpiration;

            public CachedItem(string key, T value, TimeSpan slidingExpiration, DateTime absoluteExpiration, ITimeProvider time)
            {
                Key = key;
                Value = value;
                
                _slidingExpiration = slidingExpiration;
                _absoluteExpiration = absoluteExpiration;

                SetAccessed(time);
            }

            public void SetAccessed(ITimeProvider time)
            {
                ExpirationTime = Min(_absoluteExpiration, time.Now.Add(_slidingExpiration));
            }
        }

        private readonly Dictionary<string, LinkedListNode<CachedItem>> _dic = new Dictionary<string, LinkedListNode<CachedItem>>();

        private readonly LinkedList<CachedItem> _keys = new LinkedList<CachedItem>();
        private readonly object _lock = new object();
        private readonly ITimeProvider _time;
        private int _cacheSize;
        private TimeSpan _cleanupInterval = TimeSpan.FromSeconds(60);
        private DateTime _nextCleanup;
        private readonly Action<T> _afterRemoveAction;

        public SmartCache(int cacheSize, Action<T> afterRemoveAction = null) : this(cacheSize, new RealTimeProvider())
        {
            _afterRemoveAction = afterRemoveAction;
        }

        internal SmartCache(int cacheSize, ITimeProvider timeProvider)
        {
            _cacheSize = cacheSize;
            _time = timeProvider;
            _nextCleanup = _time.Now.Add(CleanupInterval);
        }

        public int ItemCount
        {
            get
            {
                lock (_lock)
                    return _dic.Count;
            }
        }

        public int CacheSize
        {
            get
            {
                lock (_lock)
                    return _cacheSize;
            }
            set
            {
                lock (_lock)
                {
                    if (value < _cacheSize)
                    {
                        while (_dic.Count > value)
                            RemoveOldest();
                    }
                    _cacheSize = value;
                }
            }
        }

        public TimeSpan CleanupInterval
        {
            get
            {
                lock (_lock)
                    return _cleanupInterval;
            }
            set
            {
                lock (_lock)
                {
                    _cleanupInterval = value;
                    _nextCleanup = _time.Now.Add(CleanupInterval);
                }
            }
        }


        public void ClearCache()
        {
            lock (_lock)
            {
                _dic.Clear();
                _keys.Clear();
            }
        }

        public bool TryGetValue(string key, out T item, Func<T> addFunc)
        {
            return TryGetValue(key, out item, DefaultSlidingExpiration, DefaultAbsoluteExpiration, addFunc);
        }

        public bool TryGetValue(string key, out T item, TimeSpan slidingExpiration, DateTime absoluteExpiration, Func<T> addFunc)
        {
            lock (_lock)
            {
                if (TryGetValue(key, out item))
                    return true;

                item = addFunc();

                Add(key, item, slidingExpiration, absoluteExpiration);

                return true;
            }
        }

        public bool TryGetValue(string key, out T item)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            lock (_lock)
            {
                CheckCleanup();

                if (_dic.TryGetValue(key, out var node))
                {
                    if (node.Value.ExpirationTime < _time.Now)
                    {
                        Remove(node);
                        
                        item = default(T);

                        return false;
                    }

                    Promote(key);

                    item = node.Value.Value;
                    
                    return true;
                }
            }

            item = default(T);
            
            return false;
        }

        public void Remove(string key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            lock (_lock)
            {
                if (_dic.TryGetValue(key, out var node))
                {
                    Remove(node);
                }
            }
        }

        // make sure we have a lock when calling this
        // also make sure the item is in the cache
        private void Promote(string key)
        {
            LinkedListNode<CachedItem> node = _dic[key];

            _keys.Remove(node);
            _keys.AddFirst(node);

            node.Value.SetAccessed(_time);
        }

        // make sure we have a lock when calling this
        private void RemoveOldest()
        {
            var item = _keys.Last.Value;

            _dic.Remove(item.Key);
            _keys.RemoveLast();

            _afterRemoveAction?.Invoke(item.Value);
        }

        // make sure we have a lock when calling this
        private void Remove(LinkedListNode<CachedItem> node)
        {
            var item = node.Value;

            _keys.Remove(node);
            _dic.Remove(node.Value.Key);

            _afterRemoveAction?.Invoke(item.Value);
        }

        // make sure we have a writer lock when calling this
        private void CheckCleanup()
        {
            if (_time.Now < _nextCleanup)
                return;

            LinkedListNode<CachedItem> node = _keys.First;

            while (node != null)
            {
                LinkedListNode<CachedItem> next = node.Next;

                if (node.Value.ExpirationTime < _time.Now)
                    Remove(node);

                node = next;
            }

            _nextCleanup = _time.Now.Add(CleanupInterval);
        }

        public void Add(string key, T item)
        {
            Add(key, item, DefaultSlidingExpiration, DefaultAbsoluteExpiration);
        }

        public void Add(string key, T item, TimeSpan slidingExpiration)
        {
            Add(key, item, slidingExpiration, DefaultAbsoluteExpiration);
        }

        public void Add(string key, T item, DateTime absoluteExpiration)
        {
            Add(key, item, DefaultSlidingExpiration, absoluteExpiration);
        }

        public void Add(string key, T item, TimeSpan slidingExpiration, DateTime absoluteExpiration)
        {
            lock (_lock)
            {
                var cachedItem = new CachedItem(key, item, slidingExpiration, absoluteExpiration, _time);

                if (_dic.TryGetValue(key, out var node))
                {
                    node.Value = cachedItem;

                    Promote(key);
                    
                    return;
                }

                node = new LinkedListNode<CachedItem>(cachedItem);
                
                if (_dic.Count >= _cacheSize)
                    RemoveOldest();

                _keys.AddFirst(node);

                _dic[key] = node;
            }
        }
    }
}