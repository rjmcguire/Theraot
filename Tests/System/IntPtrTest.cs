﻿using NUnit.Framework;
using System;
using System.Runtime.InteropServices;
using Theraot.Core;

namespace Tests.System
{
    [TestFixture]
    internal class IntPtrTest
    {
        [Test]
        public void IntPtrAddTest()
        {
            int[] arr = { 2, 4, 6, 8, 10, 12, 14, 16, 18, 20 };
            unsafe
            {
                fixed (int* parr = arr)
                {
                    var ptr = new IntPtr(parr);
                    // Get the size of an array element.
                    const int size = sizeof(int);
                    var index = 0;
                    for (var ctr = 0; ctr < arr.Length; ctr++)
                    {
                        var newPtr = IntPtrHelper.Add(ptr, ctr * size);
                        Assert.AreEqual(arr[index], Marshal.ReadInt32(newPtr));
                        index++;
                    }
                }
            }
        }

        [Test]
        public void IntPtrSubtractTest()
        {
            int[] arr = { 2, 4, 6, 8, 10, 12, 14, 16, 18, 20 };
            unsafe
            {
                fixed (int* parr = arr)
                {
                    // Get the size of an array element.
                    const int size = sizeof(int);
                    var ptr = IntPtrHelper.Add(new IntPtr(parr), size * (arr.Length - 1));
                    var index = arr.Length - 1;
                    for (var ctr = 0; ctr < arr.Length; ctr++)
                    {
                        var newPtr = IntPtrHelper.Subtract(ptr, ctr * size);
                        Assert.AreEqual(arr[index], Marshal.ReadInt32(newPtr));
                        index--;
                    }
                }
            }
        }
    }
}