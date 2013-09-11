﻿using System;
using System.Threading;
using Theraot.Collections.ThreadSafe;
using Theraot.Core;

namespace Theraot.Threading
{
    internal class ArrayPool<T>
    {
        private const int INT_MaxCapacity = 1024;
        private const int INT_MinCapacity = 16;
        private const int INT_PoolSize = 16;
        private const int INT_WorkCapacityHint = 16;
        private static LazyBucket<FixedSizeQueueBucket<T[]>> _data;
        private static int _done;
        private static ReentryGuard _guard;
        private static WorkContext _recycle;

        static ArrayPool()
        {
            _recycle = new WorkContext("Recycler", INT_WorkCapacityHint, 1);
            _data = new LazyBucket<FixedSizeQueueBucket<T[]>>
            (
                input =>
                {
                    return new FixedSizeQueueBucket<T[]>(INT_PoolSize);
                },
                NumericHelper.Log2(INT_MaxCapacity) - NumericHelper.Log2(INT_MinCapacity)
            );
            _guard = new ReentryGuard();
            Thread.MemoryBarrier();
            Thread.VolatileWrite(ref _done, 1);
        }

        public static bool DonateArray(T[] array)
        {
            if (ReferenceEquals(array, null) || AppDomain.CurrentDomain.IsFinalizingForUnload())
            {
                //Ignore null arrays and anything on AppDomain Unload
                return false;
            }
            else
            {
                int capacity = array.Length;
                if (NumericHelper.PopulationCount(capacity) == 1)
                {
                    if (capacity < INT_MinCapacity || capacity > INT_MaxCapacity)
                    {
                        //Rejected
                        return false;
                    }
                    else
                    {
                        _recycle.AddWork
                        (
                            () =>
                            {
                                _guard.Execute(() =>
                                {
                                    int index = GetIndex(capacity);
                                    if (index < _data.Capacity)
                                    {
                                        Array.Clear(array, 0, capacity);
                                        var bucket = _data.Get(index);
                                        bucket.Add(array);
                                    }
                                });
                            }
                        ).Start();
                        return true;
                    }
                }
                else
                {
                    throw new ArgumentException("The size of the array must be a power of two.", "array");
                }
            }
        }

        public static T[] GetArray(int capacity)
        {
            if (capacity < INT_MinCapacity)
            {
                capacity = INT_MinCapacity;
            }
            else
            {
                if (capacity > INT_MaxCapacity)
                {
                    //Too big to leak it
                    return new T[capacity];
                }
                else
                {
                    capacity = NumericHelper.PopulationCount(capacity) == 1 ? capacity : NumericHelper.NextPowerOf2(capacity);
                }
            }
            if (Thread.VolatileRead(ref _done) == 1 && !_guard.IsTaken)
            {
                var promise = _guard.Execute
                (
                    () =>
                    {
                        T[] result;
                        int index = GetIndex(capacity);
                        if (index >= _data.Capacity)
                        {
                            result = new T[capacity];
                        }
                        else
                        {
                            var bucket = _data.Get(index);
                            if (!bucket.TryTake(out result))
                            {
                                result = new T[capacity];
                            }
                        }
                        return result;
                    }
                );
                return promise.Value;
            }
            else
            {
                return new T[capacity];
            }
        }

        private static int GetIndex(int capacity)
        {
            return NumericHelper.Log2(capacity) - NumericHelper.Log2(INT_MinCapacity);
        }
    }
}