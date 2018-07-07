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
using System.Threading;
using NUnit.Framework;

namespace Iridium.Cache.Test
{
    [TestFixture]
    public class SmartCache_Fixture
    {
        [Test]
        public void ThreadedTest()
        {
            SmartCache<int> cache = new SmartCache<int>(50);

            Thread[] threads = new Thread[50];

            int exceptionCount = 0;
            object exceptionCountLock = new object();

            for (int i=0;i<50;i++)
            {
                threads[i] = new Thread(data =>
                {
                    try
                    {
                        for (int i1 = 0; i1 < 1000; i1++)
                        {
                            int x;

                            if (!cache.TryGetValue(i1.ToString(), out x))
                                cache.Add(i1.ToString(), i1);
                        }
                    }
                    catch
                    {
                        lock (exceptionCountLock)
                            exceptionCount++;

                        throw;
                    }
                });

                threads[i].Start();
            }

            for (int i = 0; i < 50; i++)
                threads[i].Join();

            Assert.AreEqual(50,cache.ItemCount);
            Assert.AreEqual(0, exceptionCount);
        }

        [Test]
        public void ThreadedRemoveTest()
        {
            SmartCache<int> cache = new SmartCache<int>(50);

            Thread[] threads = new Thread[50];

            int exceptionCount = 0;
            object exceptionCountLock = new object();

            ParameterizedThreadStart t = o =>
            {
                try
                {
                    Random random = new Random();

                    for (int i1 = 0; i1 < 10000; i1++)
                    {
                        int x;

                        if (!cache.TryGetValue(i1.ToString(), out x))
                        {
                            if (random.Next() % 4 != 0)
                                cache.Remove(i1.ToString());
                            else
                                cache.Add(i1.ToString(), i1);
                        }
                        else
                        {
                            if (random.Next() % 3 == 0)
                                cache.Remove(i1.ToString());
                        }
                    }
                }
                catch
                {
                    lock (exceptionCountLock)
                        exceptionCount++;

                    throw;
                }
            };

            for (int i = 0; i < 50; i++)
            {
                threads[i] = new Thread(t);

                threads[i].Start();
            }

            for (int i = 0; i < 50; i++)
                threads[i].Join();

            Assert.AreEqual(50, cache.ItemCount);
            Assert.AreEqual(0, exceptionCount);
        }

        [Test]
        public void Test_Expiring()
        {
            MockTimeProvider time = new MockTimeProvider
            {
                UtcNow = new DateTime(2000, 1, 1)
            };

            SmartCache<int> cache = new SmartCache<int>(5, time);

            int item;

            cache.Add("1", 1, time.Now.AddMinutes(100));
            cache.Add("2", 2, time.Now.AddMinutes(200));
            cache.Add("3", 3, time.Now.AddMinutes(300));

            Assert.IsTrue(cache.TryGetValue("1", out item));
            Assert.AreEqual(3, cache.ItemCount);

            time.UtcNow += TimeSpan.FromMinutes(150);

            Assert.IsFalse(cache.TryGetValue("1", out item));
            Assert.IsTrue(cache.TryGetValue("2", out item));
            Assert.IsTrue(cache.TryGetValue("3", out item));
            Assert.AreEqual(2, cache.ItemCount);

            time.UtcNow += TimeSpan.FromMinutes(100);

            Assert.IsFalse(cache.TryGetValue("1", out item));
            Assert.IsFalse(cache.TryGetValue("2", out item));
            Assert.IsTrue(cache.TryGetValue("3", out item));
            Assert.AreEqual(1, cache.ItemCount);

            time.UtcNow += TimeSpan.FromMinutes(100);

            Assert.IsFalse(cache.TryGetValue("1", out item));
            Assert.IsFalse(cache.TryGetValue("2", out item));
            Assert.IsFalse(cache.TryGetValue("3", out item));
            Assert.AreEqual(0, cache.ItemCount);
        }
        

        [Test]
        public void Test_Remove()
        {
            SmartCache<int> cache = new SmartCache<int>(5);

            int item;

            Assert.AreEqual(0, cache.ItemCount);

            Assert.IsFalse(cache.TryGetValue("1", out item));

            cache.Add("1", 1);
            Assert.AreEqual(1, cache.ItemCount);

            cache.Add("1", 1);
            cache.Add("2", 2);
            cache.Add("3", 3);
            cache.Add("4", 4);
            cache.Add("5", 5);
            Assert.AreEqual(5, cache.ItemCount);

            cache.Remove("3");
            Assert.IsTrue(cache.TryGetValue("1", out item));
            Assert.AreEqual(1, item);
            Assert.IsTrue(cache.TryGetValue("2", out item));
            Assert.AreEqual(2, item);
            Assert.IsFalse(cache.TryGetValue("3", out item));
            Assert.IsTrue(cache.TryGetValue("4", out item));
            Assert.AreEqual(4, item);
            Assert.IsTrue(cache.TryGetValue("5", out item));
            Assert.AreEqual(5, item);
            Assert.AreEqual(4, cache.ItemCount);

            cache.Remove("1");
            Assert.IsFalse(cache.TryGetValue("1", out item));
            Assert.IsTrue(cache.TryGetValue("2", out item));
            Assert.AreEqual(2, item);
            Assert.IsFalse(cache.TryGetValue("3", out item));
            Assert.IsTrue(cache.TryGetValue("4", out item));
            Assert.AreEqual(4, item);
            Assert.IsTrue(cache.TryGetValue("5", out item));
            Assert.AreEqual(5, item);
            Assert.AreEqual(3, cache.ItemCount);

            cache.Add("3", 3);
            Assert.IsFalse(cache.TryGetValue("1", out item));
            Assert.IsTrue(cache.TryGetValue("2", out item));
            Assert.IsTrue(cache.TryGetValue("3", out item));
            Assert.IsTrue(cache.TryGetValue("4", out item));
            Assert.IsTrue(cache.TryGetValue("5", out item));
            Assert.AreEqual(4, cache.ItemCount);

            cache.Remove("6");
            Assert.IsFalse(cache.TryGetValue("1", out item));
            Assert.IsTrue(cache.TryGetValue("2", out item));
            Assert.IsTrue(cache.TryGetValue("3", out item));
            Assert.IsTrue(cache.TryGetValue("4", out item));
            Assert.IsTrue(cache.TryGetValue("5", out item));
            Assert.AreEqual(4, cache.ItemCount);
        }

        [Test]
        public void Test_Sliding()
        {
            MockTimeProvider time = new MockTimeProvider();

            time.UtcNow = new DateTime(2000, 1, 1);

            SmartCache<int> cache = new SmartCache<int>(5, time);

            int item;

            cache.Add("1", 1, TimeSpan.FromMinutes(100));
            cache.Add("2", 2, TimeSpan.FromMinutes(200));
            cache.Add("3", 3, TimeSpan.FromMinutes(300));

            time.UtcNow += TimeSpan.FromMinutes(20);

            Assert.IsTrue(cache.TryGetValue("1", out item));
            Assert.AreEqual(3, cache.ItemCount);

            time.UtcNow += TimeSpan.FromMinutes(150);

            Assert.IsFalse(cache.TryGetValue("1", out item));
            Assert.IsTrue(cache.TryGetValue("2", out item));
            Assert.AreEqual(2, cache.ItemCount);

            time.UtcNow += TimeSpan.FromDays(1);

            Assert.IsFalse(cache.TryGetValue("1", out item));
            Assert.IsFalse(cache.TryGetValue("2", out item));
            Assert.IsFalse(cache.TryGetValue("3", out item));

            Assert.AreEqual(0,cache.ItemCount);
        }

        [Test]
        public void Test1()
        {
            SmartCache<int> cache = new SmartCache<int>(5);

            int item;

            Assert.AreEqual(0, cache.ItemCount);

            Assert.IsFalse(cache.TryGetValue("1", out item));

            cache.Add("1",1);

            Assert.AreEqual(1, cache.ItemCount);

            Assert.IsTrue(cache.TryGetValue("1", out item));

            cache.Add("2", 2);
            cache.Add("3", 3);
            cache.Add("4", 4);
            cache.Add("5", 5);

            Assert.AreEqual(5, cache.ItemCount);

            Assert.IsTrue(cache.TryGetValue("1", out item));
            Assert.AreEqual(1, item);
            Assert.IsTrue(cache.TryGetValue("2", out item));
            Assert.AreEqual(2, item);
            Assert.IsTrue(cache.TryGetValue("3", out item));
            Assert.AreEqual(3, item);
            Assert.IsTrue(cache.TryGetValue("4", out item));
            Assert.AreEqual(4, item);
            Assert.IsTrue(cache.TryGetValue("5", out item));
            Assert.AreEqual(5, item);

            cache.Add("6",6);

            Assert.AreEqual(5, cache.ItemCount);

            Assert.IsFalse(cache.TryGetValue("1", out item));
            Assert.IsTrue(cache.TryGetValue("6", out item));

            Assert.IsTrue(cache.TryGetValue("3", out item));

            cache.Add("7", 7);
            cache.Add("8", 8);
            cache.Add("9", 9);

            Assert.AreEqual(5, cache.ItemCount);

            Assert.IsFalse(cache.TryGetValue("1", out item));
            Assert.IsFalse(cache.TryGetValue("2", out item));
            Assert.IsTrue(cache.TryGetValue("3", out item));
            Assert.AreEqual(3, item);
            Assert.IsFalse(cache.TryGetValue("4", out item));
            Assert.IsFalse(cache.TryGetValue("5", out item));
            Assert.IsTrue(cache.TryGetValue("6", out item));
            Assert.AreEqual(6, item);
            Assert.IsTrue(cache.TryGetValue("7", out item));
            Assert.AreEqual(7, item);
            Assert.IsTrue(cache.TryGetValue("8", out item));
            Assert.AreEqual(8, item);
            Assert.IsTrue(cache.TryGetValue("9", out item));
            Assert.AreEqual(9, item);

            Assert.IsTrue(cache.TryGetValue("7", out item));
            Assert.AreEqual(7, item);

            cache.CacheSize = 2;

            Assert.AreEqual(2, cache.ItemCount);

            Assert.IsFalse(cache.TryGetValue("1", out item));
            Assert.IsFalse(cache.TryGetValue("2", out item));
            Assert.IsFalse(cache.TryGetValue("3", out item));
            Assert.IsFalse(cache.TryGetValue("4", out item));
            Assert.IsFalse(cache.TryGetValue("5", out item));
            Assert.IsFalse(cache.TryGetValue("6", out item));
            Assert.IsTrue(cache.TryGetValue("7", out item));
            Assert.IsFalse(cache.TryGetValue("8", out item));
            Assert.IsTrue(cache.TryGetValue("9", out item));

            cache.ClearCache();

            Assert.AreEqual(0,cache.ItemCount);

            Assert.IsFalse(cache.TryGetValue("1", out item));
            Assert.IsFalse(cache.TryGetValue("2", out item));
            Assert.IsFalse(cache.TryGetValue("3", out item));
            Assert.IsFalse(cache.TryGetValue("4", out item));
            Assert.IsFalse(cache.TryGetValue("5", out item));
            Assert.IsFalse(cache.TryGetValue("6", out item));
            Assert.IsFalse(cache.TryGetValue("7", out item));
            Assert.IsFalse(cache.TryGetValue("8", out item));
            Assert.IsFalse(cache.TryGetValue("9", out item));
        }
            
    }
}