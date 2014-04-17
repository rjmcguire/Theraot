using System;
using System.Collections.Generic;
using System.Threading;
using Theraot.Threading;

namespace Theraot.Collections.ThreadSafe
{
    /// <summary>
    /// Represent a thread-safe lock-free queue.
    /// </summary>
    /// <typeparam name="T">The type of the item.</typeparam>
    public sealed class QueueBucket<T> : IEnumerable<T>
    {
        private const int INT_DefaultCapacity = 64;

        private int _copyingThreads;
        private int _copySourcePosition;
        private int _count;
        private FixedSizeQueueBucket<T> _entriesNew;
        private FixedSizeQueueBucket<T> _entriesOld;
        private volatile int _revision;
        private int _status;
        private int _workingThreads;

        /// <summary>
        /// Initializes a new instance of the <see cref="QueueBucket{T}" /> class.
        /// </summary>
        public QueueBucket()
            : this(INT_DefaultCapacity)
        {
            //Empty
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="QueueBucket{T}" /> class.
        /// </summary>
        /// <param name="initialCapacity">The initial capacity.</param>
        public QueueBucket(int initialCapacity)
        {
            _entriesOld = null;
            _entriesNew = new FixedSizeQueueBucket<T>(initialCapacity);
        }

        /// <summary>
        /// Gets the capacity.
        /// </summary>
        public int Capacity
        {
            get
            {
                return _entriesNew.Capacity;
            }
        }

        /// <summary>
        /// Gets the number of keys actually contained.
        /// </summary>
        public int Count
        {
            get
            {
                return _count;
            }
        }

        /// <summary>
        /// Adds the specified item at the front.
        /// </summary>
        /// <param name="item">The item.</param>
        public void Add(T item)
        {
            bool result = false;
            while (true)
            {
                if (IsOperationSafe())
                {
                    var entries = ThreadingHelper.VolatileRead(ref _entriesNew);
                    bool done = false;
                    try
                    {
                        Interlocked.Increment(ref _workingThreads);
                        result |= entries.Add(item);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _workingThreads);
                        if (result)
                        {
                            Interlocked.Increment(ref _count);
                            done = true;
                        }
                        else
                        {
                            var oldStatus = Interlocked.CompareExchange(ref _status, (int)BucketStatus.GrowRequested, (int)BucketStatus.Free);
                            if (oldStatus == (int)BucketStatus.Free)
                            {
                                _revision++;
                            }
                        }
                    }
                    if (done)
                    {
                        return;
                    }
                }
                else
                {
                    CooperativeGrow();
                }
            }
        }

        /// <summary>
        /// Removes all the elements.
        /// </summary>
        public void Clear()
        {
            ThreadingHelper.VolatileWrite(ref _entriesOld, null);
            ThreadingHelper.VolatileWrite(ref _entriesNew, new FixedSizeQueueBucket<T>(INT_DefaultCapacity));
            Thread.VolatileWrite(ref _status, (int)BucketStatus.Free);
            Thread.VolatileWrite(ref _count, 0);
            _revision++;
        }

        /// <summary>
        /// Returns an <see cref="System.Collections.Generic.IEnumerator{T}" /> that allows to iterate through the collection.
        /// </summary>
        /// <returns>
        /// An <see cref="System.Collections.Generic.IEnumerator{T}" /> object that can be used to iterate through the collection.
        /// </returns>
        public IEnumerator<T> GetEnumerator()
        {
            return _entriesNew.GetEnumerator();
        }

        /// <summary>
        /// Gets the items contained in this object.
        /// </summary>
        public IList<T> GetValues()
        {
            return _entriesNew.GetValues();
        }

        /// <summary>
        /// Gets the values contained in this object.
        /// </summary>
        public IList<TOutput> GetValues<TOutput>(Converter<T, TOutput> converter)
        {
            return _entriesNew.GetValues(converter);
        }

        /// <summary>
        /// Returns the next item to be taken from the back without removing it.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">No more items to be taken.</exception>
        public T Peek()
        {
            T result = default(T);
            while (true)
            {
                int revision = _revision;
                if (IsOperationSafe())
                {
                    var entries = ThreadingHelper.VolatileRead(ref _entriesNew);
                    bool done = false;
                    try
                    {
                        result = entries.Peek();
                    }
                    finally
                    {
                        var isOperationSafe = IsOperationSafe(entries, revision);
                        if (isOperationSafe)
                        {
                            done = true;
                        }
                    }
                    if (done)
                    {
                        return result;
                    }
                }
                else
                {
                    CooperativeGrow();
                }
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Tries the retrieve the item at an specified index.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <param name="item">The item.</param>
        /// <returns>
        ///   <c>true</c> if the value was retrieved; otherwise, <c>false</c>.
        /// </returns>
        /// <remarks>
        /// Although items are ordered, they are not guaranteed to start at index 0.
        /// </remarks>
        public bool TryGet(int index, out T item)
        {
            item = default(T);
            bool result = false;
            while (true)
            {
                int revision = _revision;
                if (IsOperationSafe())
                {
                    var entries = ThreadingHelper.VolatileRead(ref _entriesNew);
                    bool done = false;
                    try
                    {
                        T tmpItem;
                        if (entries.TryGet(index, out tmpItem))
                        {
                            item = tmpItem;
                            result = true;
                        }
                    }
                    finally
                    {
                        var isOperationSafe = IsOperationSafe(entries, revision);
                        if (isOperationSafe)
                        {
                            done = true;
                        }
                    }
                    if (done)
                    {
                        return result;
                    }
                }
                else
                {
                    CooperativeGrow();
                }
            }
        }

        /// <summary>
        /// Attempts to retrieve and remove the next item from the back.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <returns>
        ///   <c>true</c> if the item was taken; otherwise, <c>false</c>.
        /// </returns>
        public bool TryTake(out T item)
        {
            item = default(T);
            bool result = false;
            while (true)
            {
                if (IsOperationSafe())
                {
                    var entries = ThreadingHelper.VolatileRead(ref _entriesNew);
                    try
                    {
                        Interlocked.Increment(ref _workingThreads);
                        T tmpItem;
                        if (entries.TryTake(out tmpItem))
                        {
                            item = tmpItem;
                            result = true;
                        }
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _workingThreads);
                        if (result)
                        {
                            Interlocked.Decrement(ref _count);
                        }
                    }
                    return result;
                }
                else
                {
                    CooperativeGrow();
                }
            }
        }

        private void CooperativeGrow()
        {
            int status;
            do
            {
                status = Thread.VolatileRead(ref _status);
                int oldStatus;
                switch (status)
                {
                    case (int)BucketStatus.GrowRequested:
                        var priority = Thread.CurrentThread.Priority;
                        oldStatus = Interlocked.CompareExchange(ref _status, (int)BucketStatus.Waiting, (int)BucketStatus.GrowRequested);
                        if (oldStatus == (int)BucketStatus.GrowRequested)
                        {
                            try
                            {
                                Thread.CurrentThread.Priority = ThreadPriority.Highest;
                                Thread.VolatileWrite(ref _copySourcePosition, -1);
                                var newCapacity = _entriesNew.Capacity * 2;
                                _entriesOld = Interlocked.Exchange(ref _entriesNew, new FixedSizeQueueBucket<T>(newCapacity));
                                oldStatus = Interlocked.CompareExchange(ref _status, (int)BucketStatus.Copy, (int)BucketStatus.Waiting);
                            }
                            finally
                            {
                                Thread.CurrentThread.Priority = priority;
                                _revision++;
                            }
                        }
                        break;

                    case (int)BucketStatus.Waiting:
                        ThreadingHelper.SpinWaitWhile(ref _status, (int)BucketStatus.Waiting);
                        break;

                    case (int)BucketStatus.Copy:
                        _revision++;
                        if (Thread.VolatileRead(ref _workingThreads) > 0)
                        {
                            ThreadingHelper.SpinWaitUntil(ref _workingThreads, 0);
                        }
                        var old = _entriesOld;
                        if (old != null)
                        {
                            _revision++;
                            Interlocked.Increment(ref _copyingThreads);

                            int capacity = old.Capacity;
                            int offset = old.IndexDequeue;

                            int sourceIndex = Interlocked.Increment(ref _copySourcePosition);
                            while (sourceIndex < capacity)
                            {
                                T item;
                                if (old.TryGet((sourceIndex + offset) & (capacity - 1), out item))
                                {
                                    //HACK
                                    bool dummy;
                                    _entriesNew.Set(sourceIndex, item, out dummy);
                                }
                                sourceIndex = Interlocked.Increment(ref _copySourcePosition);
                            }
                            Interlocked.CompareExchange(ref _status, (int)BucketStatus.Waiting, (int)BucketStatus.Copy);
                            _revision++;
                            if (Interlocked.Decrement(ref _copyingThreads) == 0)
                            {
                                //HACK
                                _entriesNew.IndexEnqueue = capacity;
                                Interlocked.CompareExchange(ref _status, (int)BucketStatus.CopyCleanup, (int)BucketStatus.Waiting);
                            }
                        }
                        break;

                    case (int)BucketStatus.CopyCleanup:
                        oldStatus = Interlocked.CompareExchange(ref _status, (int)BucketStatus.Waiting, (int)BucketStatus.CopyCleanup);
                        if (oldStatus == (int)BucketStatus.CopyCleanup)
                        {
                            _revision++;
                            Interlocked.Exchange(ref _entriesOld, null);
                            Thread.Sleep(1);
                            Interlocked.CompareExchange(ref _status, (int)BucketStatus.Free, (int)BucketStatus.Waiting);
                        }
                        break;
                }
            }
            while (status != (int)BucketStatus.Free);
        }

        private bool IsOperationSafe(object entries, int revision)
        {
            bool check = _revision != revision;
            if (check)
            {
                return false;
            }
            else
            {
                var newEntries = Interlocked.CompareExchange(ref _entriesNew, null, null);
                if (entries == newEntries)
                {
                    var newStatus = Interlocked.CompareExchange(ref _status, (int)BucketStatus.Free, (int)BucketStatus.Free);
                    if (newStatus == (int)BucketStatus.Free)
                    {
                        if (Thread.VolatileRead(ref _copyingThreads) > 0)
                        {
                            _revision++;
                            return false;
                        }
                        else
                        {
                            return true;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
        }

        private bool IsOperationSafe()
        {
            var newStatus = Interlocked.CompareExchange(ref _status, (int)BucketStatus.Free, (int)BucketStatus.Free);
            if (newStatus == (int)BucketStatus.Free)
            {
                if (Thread.VolatileRead(ref _copyingThreads) > 0)
                {
                    _revision++;
                    return false;
                }
                else
                {
                    return true;
                }
            }
            else
            {
                return false;
            }
        }
    }
}